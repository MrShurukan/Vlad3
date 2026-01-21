using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TSLib;
using TSLib.Audio;
using TSLib.Full;
using TSLib.Helper;
using TSLib.Messages;
using Vlad3.Core.Abstractions;
using Vlad3.Core.Models;

namespace Vlad3.Bots.TeamSpeak;

public sealed class TeamSpeakAudioBot : IAudioBot
{
    private const string AddressSetting = "address";
    private const string HostSetting = "host";
    private const string PortSetting = "port";
    private const string ConnectTimeoutSecondsSetting = "connectTimeoutSeconds";
    private const string NicknameSetting = "nickname";
    private const string ServerPasswordSetting = "serverPassword";
    private const string DefaultChannelSetting = "defaultChannel";
    private const string ChannelPasswordSetting = "channelPassword";
    private const string IdentitySetting = "identity";
    private const string IdentityPrivateKeySetting = "identityPrivateKey";
    private const string IdentityOffsetSetting = "identityOffset";
    private const string FfmpegPathSetting = "ffmpegPath";
    private const string AutoNextSetting = "autoNext";

    private readonly BotConfiguration _configuration;
    private readonly ILogger<TeamSpeakAudioBot> _logger;
    private readonly TsFullClient _client;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly Id _logId;
    private PlaybackSession? _playback;
    private string? _connectedChannelId;
    private string? _connectedChannelName;
    private BotState _state;

    public TeamSpeakAudioBot(BotConfiguration configuration, ILogger<TeamSpeakAudioBot> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _logId = new Id(configuration.Id.GetHashCode());
        _client = new TsFullClient();

        var autoNextEnabled = TryGetSettingBool(AutoNextSetting, out var enabled) ? enabled : true;
        _state = new BotState(BotConnectionState.Disconnected, BotPlaybackState.Stopped, null, null, null, null, autoNextEnabled ? PlaylistMovementType.AutoNext : PlaylistMovementType.None);

        _client.OnEachTextMessage += (_, message) => _ = HandleTextMessageAsync(message);
        _client.OnDisconnected += (_, args) =>
        {
            if (args is null)
            {
                return;
            }

            var errorText = args.Error?.ErrorFormat() ?? "unknown";
            _logger.LogInformation(
                "TeamSpeak bot {Label} disconnected: {Reason} ({Error})",
                Label,
                args.ExitReason,
                errorText);
            _state = _state with
            {
                ConnectionState = BotConnectionState.Disconnected,
                ConnectedChannelId = null,
                ConnectedChannelName = null,
                PlaybackState = BotPlaybackState.Stopped
            };
        };
        _client.OnErrorEvent += (_, error)
            => _logger.LogWarning("TeamSpeak bot {Label} error: {Error}", Label, error.ErrorFormat());
    }

    public string Id => _configuration.Id;
    public string Type => _configuration.Type;
    public string Label => _configuration.Label;
    public BotState State => _state;

    public event Func<AudioBotCommand, Task>? CommandReceived;

    public async Task<IReadOnlyList<AudioChannelInfo>> GetAvailableChannelsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        if (!_client.Connected)
        {
            return Array.Empty<AudioChannelInfo>();
        }

        var response = _client.ChannelList();
        if (!response.Ok)
        {
            _logger.LogWarning("TeamSpeak bot {Label} failed to fetch channels: {Error}", Label, response.Error.ErrorFormat());
            return Array.Empty<AudioChannelInfo>();
        }

        return response.Value
            .Select(channel => new AudioChannelInfo(channel.ChannelId.ToString(), channel.Name))
            .OrderBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task ConnectAsync(string channelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new ArgumentException("ChannelId is required.", nameof(channelId));
        }

        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopPlaybackInternalAsync().ConfigureAwait(false);
            await MoveToChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopPlaybackInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client.Connected)
            {
                _client.Disconnect();
            }

            _connectedChannelId = null;
            _connectedChannelName = null;
            _state = _state with
            {
                ConnectionState = BotConnectionState.Disconnected,
                ConnectedChannelId = null,
                ConnectedChannelName = null,
                PlaybackState = BotPlaybackState.Stopped
            };
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task PlayAsync(AudioTrackInfo track, CancellationToken cancellationToken = default)
    {
        if (track is null)
        {
            throw new ArgumentNullException(nameof(track));
        }

        if (!File.Exists(track.FilePath))
        {
            throw new FileNotFoundException("Audio file not found.", track.FilePath);
        }

        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        if (_state.ConnectionState == BotConnectionState.Disconnected)
        {
            throw new InvalidOperationException("Bot is not connected to a channel.");
        }

        if (_connectedChannelId is null)
        {
            TryUpdateConnectedChannelFromWhoAmI();
        }

        if (_connectedChannelId is null)
        {
            throw new InvalidOperationException("Bot is not connected to a channel.");
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopPlaybackInternalAsync().ConfigureAwait(false);
            var playback = StartPlayback(track);
            _playback = playback;
            _state = _state with { PlaybackState = BotPlaybackState.Playing };
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopPlaybackInternalAsync().ConfigureAwait(false);
            _state = _state with { PlaybackState = BotPlaybackState.Stopped };
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task RegisterCommandsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("TeamSpeak bot {Label} ready for text commands", Label);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopPlaybackInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }

        await _connectionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_client.Connected)
            {
                _client.Disconnect();
            }
        }
        finally
        {
            _connectionGate.Release();
        }

        _client.Dispose();
        _logger.LogInformation("TeamSpeak bot {Label} disposed", Label);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.Connected)
        {
            return;
        }

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client.Connected)
            {
                return;
            }

            var connectionData = BuildConnectionData();
            try
            {
                _client.Connect(connectionData);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("TeamSpeak connect failed.", ex);
            }

            await WaitForConnectedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private ConnectionDataFull BuildConnectionData()
    {
        var address = ResolveAddress();
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException("TeamSpeak address is not configured.");
        }

        var nickname = TryGetSetting(NicknameSetting, out var rawNickname)
            ? rawNickname
            : Label;

        var defaultChannel = TryGetSetting(DefaultChannelSetting, out var rawChannel)
            ? rawChannel
            : string.Empty;

        var connectionData = new ConnectionDataFull
        {
            Address = address,
            Identity = ResolveIdentity(),
            VersionSign = OperatingSystem.IsLinux() ? VersionSign.VER_LIN_3_X_X : VersionSign.VER_WIN_3_X_X,
            Username = nickname,
            ServerPassword = TryGetSetting(ServerPasswordSetting, out var rawPassword)
                ? Password.FromPlain(rawPassword)
                : Password.FromPlain(string.Empty),
            DefaultChannel = defaultChannel,
            DefaultChannelPassword = TryGetSetting(ChannelPasswordSetting, out var rawChannelPassword)
                ? Password.FromPlain(rawChannelPassword)
                : Password.FromPlain(string.Empty),
            LogId = _logId
        };

        return connectionData;
    }

    private async Task MoveToChannelAsync(string channelId, CancellationToken cancellationToken)
    {
        if (!TryParseChannelId(channelId, out var channelKey))
        {
            throw new ArgumentException("ChannelId must be a numeric TeamSpeak channel id.", nameof(channelId));
        }

        var result = _client.ClientMove(
            _client.ClientId,
            channelKey,
            TryGetSetting(ChannelPasswordSetting, out var password) ? password : null);
        if (!result.Ok)
        {
            throw new InvalidOperationException($"TeamSpeak channel move failed: {result.Error.ErrorFormat()}");
        }

        _connectedChannelId = channelId;
        _connectedChannelName = await ResolveChannelNameAsync(channelId, cancellationToken).ConfigureAwait(false);
        _state = _state with
        {
            ConnectionState = BotConnectionState.Connected,
            ConnectedChannelId = _connectedChannelId,
            ConnectedChannelName = _connectedChannelName
        };
    }

    private async Task<string> ResolveChannelNameAsync(string channelId, CancellationToken cancellationToken)
    {
        try
        {
            var channels = await GetAvailableChannelsAsync(cancellationToken).ConfigureAwait(false);
            return channels.FirstOrDefault(channel => channel.Id == channelId)?.Name ?? channelId;
        }
        catch
        {
            return channelId;
        }
    }

    private PlaybackSession StartPlayback(AudioTrackInfo track)
    {
        var process = CreateFfmpegProcess(track.FilePath);
        var producer = new StreamAudioProducer(process.StandardOutput.BaseStream);
        var encoder = new EncoderPipe(Codec.OpusMusic);
        var metaPipe = new StaticMetaPipe();
        metaPipe.SetVoice();
        encoder.OutStream = metaPipe;
        metaPipe.OutStream = _client;

        var timedPipe = new PreciseTimedPipe(producer, encoder)
        {
            Paused = false
        };
        timedPipe.Initialize(encoder, _logId);

        return new PlaybackSession(
            process,
            producer,
            timedPipe,
            encoder,
            metaPipe,
            OnPlaybackCompleted);
    }

    private void OnPlaybackCompleted(PlaybackSession session, bool completedNaturally)
    {
        if (ReferenceEquals(_playback, session))
        {
            _playback = null;
        }

        _state = _state with { PlaybackState = BotPlaybackState.Stopped };
        if (completedNaturally)
        {
            _ = RaiseCommandAsync(new AudioBotCommand(BotCommandType.PlaybackFinished));
        }
    }

    private async Task StopPlaybackInternalAsync()
    {
        var playback = _playback;
        if (playback is null)
        {
            return;
        }

        _playback = null;
        await playback.StopAsync().ConfigureAwait(false);
    }

    private async Task HandleTextMessageAsync(TextMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Message))
        {
            return;
        }

        if (message.InvokerId == _client.ClientId)
        {
            return;
        }

        var raw = message.Message.Trim();
        if (!raw.StartsWith('!') && !raw.StartsWith('/'))
        {
            return;
        }

        var parts = raw.TrimStart('!', '/')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }

        var command = parts[0].ToLowerInvariant();
        AudioBotCommand? botCommand = command switch
        {
            "play" when parts.Length >= 2 => new AudioBotCommand(BotCommandType.Play, parts[1], parts.Length >= 3 ? parts[2] : null),
            "stop" => new AudioBotCommand(BotCommandType.Stop),
            "next" => new AudioBotCommand(BotCommandType.Next),
            "previous" or "prev" => new AudioBotCommand(BotCommandType.Previous),
            "connect" when parts.Length >= 2 => new AudioBotCommand(BotCommandType.Connect, ChannelId: parts[1]),
            "join" => TryBuildJoinCommand(message),
            "disconnect" => new AudioBotCommand(BotCommandType.Disconnect),
            _ => null
        };

        if (botCommand is null)
        {
            return;
        }

        await RaiseCommandAsync(botCommand).ConfigureAwait(false);
    }

    private Task RaiseCommandAsync(AudioBotCommand command)
    {
        var handler = CommandReceived;
        if (handler is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            var tasks = handler.GetInvocationList()
                .OfType<Func<AudioBotCommand, Task>>()
                .Select(h => h(command));
            return Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TeamSpeak bot {Label} failed to broadcast command {CommandType}", Label, command.Type);
            return Task.CompletedTask;
        }
    }

    private AudioBotCommand? TryBuildJoinCommand(TextMessage message)
    {
        if (_client.Book.Clients.TryGetValue(message.InvokerId, out var client))
        {
            var channelId = client.Channel.Value.ToString(CultureInfo.InvariantCulture);
            return new AudioBotCommand(BotCommandType.Connect, ChannelId: channelId);
        }

        _logger.LogWarning("TeamSpeak bot {Label} cannot resolve invoker channel for join command", Label);
        return null;
    }

    private void TryUpdateConnectedChannelFromWhoAmI()
    {
        if (!_client.Connected)
        {
            return;
        }

        var whoAmI = _client.WhoAmI();
        if (!whoAmI.Ok)
        {
            _logger.LogWarning("TeamSpeak bot {Label} failed to resolve channel: {Error}", Label, whoAmI.Error.ErrorFormat());
            return;
        }

        var channelId = whoAmI.Value.ChannelId;
        if (channelId.Equals(ChannelId.Null))
        {
            return;
        }

        _connectedChannelId = channelId.Value.ToString(CultureInfo.InvariantCulture);
        if (_client.Book.Channels.TryGetValue(channelId, out var channel))
        {
            _connectedChannelName = channel.Name;
        }
        else
        {
            _connectedChannelName = _connectedChannelId;
        }

        _state = _state with
        {
            ConnectedChannelId = _connectedChannelId,
            ConnectedChannelName = _connectedChannelName
        };
    }

    private async Task WaitForConnectedAsync(CancellationToken cancellationToken)
    {
        var timeout = ResolveConnectTimeout();
        var start = Stopwatch.StartNew();

        while (!_client.Connected)
        {
            if (start.Elapsed > timeout)
            {
                _state = _state with
                {
                    ConnectionState = BotConnectionState.Disconnected,
                    ConnectedChannelId = null,
                    ConnectedChannelName = null
                };
                throw new TimeoutException("TeamSpeak connection timeout.");
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        _state = _state with { ConnectionState = BotConnectionState.Connected };
        TryUpdateConnectedChannelFromWhoAmI();
    }

    private TimeSpan ResolveConnectTimeout()
    {
        if (TryGetSetting(ConnectTimeoutSecondsSetting, out var raw)
            && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.FromSeconds(30);
    }

    private static bool TryParseChannelId(string channelId, out ChannelId channelKey)
    {
        if (ulong.TryParse(channelId, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            channelKey = (ChannelId)value;
            return true;
        }

        channelKey = default;
        return false;
    }

    private string? ResolveAddress()
    {
        if (TryGetSetting(AddressSetting, out var address))
        {
            return address;
        }

        if (TryGetSetting(HostSetting, out var host))
        {
            var port = TryGetSetting(PortSetting, out var rawPort) && int.TryParse(rawPort, NumberStyles.None, CultureInfo.InvariantCulture, out var portValue)
                ? portValue
                : 9987;
            return $"{host}:{port}";
        }

        return null;
    }

    private IdentityData ResolveIdentity()
    {
        if (TryGetSetting(IdentitySetting, out var identityValue))
        {
            var imported = TsCrypt.DeobfuscateAndImportTsIdentity(identityValue);
            if (imported.Ok)
            {
                return imported.Value;
            }

            _logger.LogWarning("TeamSpeak bot {Label} failed to import identity, using generated one", Label);
        }

        if (TryGetSetting(IdentityPrivateKeySetting, out var privateKey))
        {
            var offset = TryGetSetting(IdentityOffsetSetting, out var rawOffset)
                && ulong.TryParse(rawOffset, NumberStyles.None, CultureInfo.InvariantCulture, out var offsetValue)
                ? offsetValue
                : 0;
            var imported = TsCrypt.LoadIdentityDynamic(privateKey, offset);
            if (imported.Ok)
            {
                return imported.Value;
            }

            _logger.LogWarning("TeamSpeak bot {Label} failed to import private key identity, using generated one", Label);
        }

        return TsCrypt.GenerateNewIdentity();
    }

    private bool TryGetSetting(string key, out string value)
    {
        if (_configuration.Settings.TryGetValue(key, out var raw)
            && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private bool TryGetSettingBool(string key, out bool value)
    {
        if (TryGetSetting(key, out var raw)
            && bool.TryParse(raw, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = false;
        return false;
    }

    private Process CreateFfmpegProcess(string inputPath)
    {
        var ffmpegPath = TryGetSetting(FfmpegPathSetting, out var configuredPath)
            ? configuredPath
            : "ffmpeg";

        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var arguments = startInfo.ArgumentList;
        arguments.Add("-hide_banner");
        arguments.Add("-loglevel");
        arguments.Add("error");
        arguments.Add("-i");
        arguments.Add(inputPath);
        arguments.Add("-ac");
        arguments.Add("2");
        arguments.Add("-f");
        arguments.Add("s16le");
        arguments.Add("-ar");
        arguments.Add("48000");
        arguments.Add("pipe:1");

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                _logger.LogWarning("ffmpeg: {Message}", args.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        return process;
    }

    private sealed class PlaybackSession
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Process _process;
        private readonly StreamAudioProducer _producer;
        private readonly PreciseTimedPipe _timedPipe;
        private readonly EncoderPipe _encoder;
        private readonly StaticMetaPipe _metaPipe;
        private readonly Action<PlaybackSession, bool> _onCompleted;
        private readonly Task _monitorTask;
        private int _stopRequested;
        private int _disposed;

        public PlaybackSession(
            Process process,
            StreamAudioProducer producer,
            PreciseTimedPipe timedPipe,
            EncoderPipe encoder,
            StaticMetaPipe metaPipe,
            Action<PlaybackSession, bool> onCompleted)
        {
            _process = process;
            _producer = producer;
            _timedPipe = timedPipe;
            _encoder = encoder;
            _metaPipe = metaPipe;
            _onCompleted = onCompleted;
            _monitorTask = Task.Run(MonitorAsync);
        }

        private async Task MonitorAsync()
        {
            var completedNaturally = false;
            try
            {
                await _process.WaitForExitAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
                completedNaturally = _stopRequested == 0 && _process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Cleanup();
                _onCompleted(this, completedNaturally);
            }
        }

        public async Task StopAsync()
        {
            Interlocked.Exchange(ref _stopRequested, 1);
            _cancellationTokenSource.Cancel();
            TryKillProcess();
            Cleanup();
            try
            {
                await _monitorTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private void Cleanup()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            try
            {
                _timedPipe.Dispose();
            }
            catch
            {
            }

            try
            {
                _encoder.Dispose();
            }
            catch
            {
            }

            try
            {
                _producer.Dispose();
            }
            catch
            {
            }

            try
            {
                _metaPipe.OutStream = null;
                _metaPipe.SetNone();
            }
            catch
            {
            }

            try
            {
                _process.Dispose();
            }
            catch
            {
            }
        }

        private void TryKillProcess()
        {
            if (_process is { HasExited: false })
            {
                try
                {
                    _process.Kill(true);
                }
                catch
                {
                }
            }
        }
    }
}
