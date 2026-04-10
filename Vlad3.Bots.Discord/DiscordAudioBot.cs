using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using Vlad3.Core.Abstractions;
using Vlad3.Core.Models;

namespace Vlad3.Bots.Discord;

public sealed class DiscordAudioBot : IAudioBot
{
    private const string GuildIdSetting = "guildId";
    private const string CommandGuildIdSetting = "commandGuildId";
    private const string FfmpegPathSetting = "ffmpegPath";

    private readonly BotConfiguration _configuration;
    private readonly ILogger<DiscordAudioBot> _logger;
    private readonly GatewayClient _client;
    private readonly ApplicationCommandService<ApplicationCommandContext> _commandService;
    private readonly NetCordLoggerAdapter _netCordLogger;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _voiceGate = new(1, 1);
    private Task? _startTask;
    private VoiceClient? _voiceClient;
    private PlaybackSession? _playback;
    private PlaybackSession? _effectPlayback;
    private double _mainPlaybackStartPositionSeconds;
    private string? _connectedChannelId;
    private string? _connectedChannelName;
    private ulong? _connectedGuildId;
    private BotState _state;

    public DiscordAudioBot(BotConfiguration configuration, ILogger<DiscordAudioBot> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _netCordLogger = new NetCordLoggerAdapter(logger);
        _state = new BotState(BotConnectionState.Disconnected, BotPlaybackState.Stopped, null, null, null, null, PlaylistMovementType.AutoNext);
        
        _client = new GatewayClient(new BotToken(configuration.ApiKey), new GatewayClientConfiguration
        {
            Intents = GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates,
            CacheProvider = ConcurrentGatewayClientCacheProvider.Empty,
            Logger = _netCordLogger,
        });

        _client.Ready += args =>
        {
            _logger.LogInformation("Discord bot {Label} connected as {User}", Label, args.User.Username);
            return default;
        };
        _client.InteractionCreate += HandleInteractionCreateAsync;

        _commandService = new ApplicationCommandService<ApplicationCommandContext>();
        RegisterSlashCommands();
    }

    public string Id => _configuration.Id;
    public string Type => _configuration.Type;
    public string Label => _configuration.Label;
    public BotState State => _state;

    public event Func<AudioBotCommand, Task>? CommandReceived;
    public event Func<Task>? SoundEffectFinished;

    public async Task<IReadOnlyList<AudioChannelInfo>> GetAvailableChannelsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync().ConfigureAwait(false);
        var guildId = ResolveGuildId();
        if (guildId is null)
        {
            return Array.Empty<AudioChannelInfo>();
        }

        var channels = await _client.Rest.GetGuildChannelsAsync(guildId.Value, cancellationToken: cancellationToken).ConfigureAwait(false);
        return channels
            .OfType<IVoiceGuildChannel>()
            .Select(channel => new AudioChannelInfo(channel.Id.ToString(CultureInfo.InvariantCulture), channel.Name))
            .OrderBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task ConnectAsync(string channelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new ArgumentException("ChannelId is required.", nameof(channelId));
        }

        await EnsureStartedAsync().ConfigureAwait(false);
        var channel = await ResolveVoiceChannelAsync(channelId, cancellationToken).ConfigureAwait(false);

        await _voiceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopPlaybackInternalAsync().ConfigureAwait(false);
            await DisconnectInternalAsync(cancellationToken).ConfigureAwait(false);

            var voiceClient = await _client.JoinVoiceChannelAsync(
                channel.GuildId,
                channel.Id,
                new VoiceClientConfiguration
                {
                    Logger = _netCordLogger,
                },
                cancellationToken).ConfigureAwait(false);

            await voiceClient.StartAsync(cancellationToken).ConfigureAwait(false);

            _voiceClient = voiceClient;
            _connectedGuildId = channel.GuildId;
            _connectedChannelId = channel.Id.ToString(CultureInfo.InvariantCulture);
            _connectedChannelName = channel.Name;

            _state = _state with
            {
                ConnectionState = BotConnectionState.Connected,
                ConnectedChannelId = _connectedChannelId,
                ConnectedChannelName = _connectedChannelName
            };
        }
        finally
        {
            _voiceGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _voiceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopPlaybackInternalAsync().ConfigureAwait(false);
            await DisconnectInternalAsync(cancellationToken).ConfigureAwait(false);

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
            _voiceGate.Release();
        }
    }

    public async Task PlayAsync(AudioTrackInfo track, double? startPositionSeconds = null, CancellationToken cancellationToken = default)
    {
        if (track is null)
        {
            throw new ArgumentNullException(nameof(track));
        }

        if (!File.Exists(track.FilePath))
        {
            throw new FileNotFoundException("Audio file not found.", track.FilePath);
        }

        await EnsureStartedAsync().ConfigureAwait(false);

        if (_voiceClient is null)
        {
            throw new InvalidOperationException("Bot is not connected to a voice channel.");
        }

        await _voiceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopPlaybackInternalAsync().ConfigureAwait(false);

            var voiceClient = _voiceClient;
            await voiceClient!.EnterSpeakingStateAsync(new SpeakingProperties(SpeakingFlags.Microphone), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var playback = new PlaybackSession(track);
            _playback = playback;
            _mainPlaybackStartPositionSeconds = startPositionSeconds ?? 0;
            _state = _state with { PlaybackState = BotPlaybackState.Playing };

            playback.Start(
                async token =>
                {
                    using var process = CreateFfmpegProcess(track.FilePath, startPositionSeconds);
                    playback.AttachProcess(process);

                    await using var opusStream = new OpusEncodeStream(
                        voiceClient.CreateVoiceStream(),
                        PcmFormat.Short,
                        VoiceChannels.Stereo,
                        OpusApplication.Audio);

                    const int bufferSize = 8192;
                    var buffer = new byte[bufferSize];
                    int read;
                    while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(0, bufferSize), token).ConfigureAwait(false)) > 0)
                    {
                        playback.AddBytesRead(read);
                        await opusStream.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    }
                    await opusStream.FlushAsync(token).ConfigureAwait(false);
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                },
                completedNaturally =>
                {
                    if (_playback == playback)
                    {
                        _playback = null;
                    }

                    _state = _state with { PlaybackState = BotPlaybackState.Stopped };
                    if (completedNaturally)
                    {
                        _ = RaiseCommandAsync(new AudioBotCommand(BotCommandType.PlaybackFinished));
                    }
                },
                exception => _logger.LogWarning(exception, "Discord bot {Label} failed to play {Track}", Label, track.Name));
        }
        finally
        {
            _voiceGate.Release();
        }
    }

    public async Task PlaySoundEffectAsync(AudioTrackInfo track, Action<double>? onMainPlaybackPaused = null, CancellationToken cancellationToken = default)
    {
        if (track is null)
        {
            throw new ArgumentNullException(nameof(track));
        }

        if (!File.Exists(track.FilePath))
        {
            throw new FileNotFoundException("Audio file not found.", track.FilePath);
        }

        await EnsureStartedAsync().ConfigureAwait(false);
        if (_voiceClient is null)
        {
            throw new InvalidOperationException("Bot is not connected to a voice channel.");
        }

        await _voiceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mainPlayback = _playback;
            if (mainPlayback is not null)
            {
                var position = _mainPlaybackStartPositionSeconds + mainPlayback.PositionSeconds;
                onMainPlaybackPaused?.Invoke(position);
            }
            await StopPlaybackInternalAsync().ConfigureAwait(false);

            var voiceClient = _voiceClient;
            await voiceClient!.EnterSpeakingStateAsync(new SpeakingProperties(SpeakingFlags.Microphone), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var effectSession = new PlaybackSession(track);
            _effectPlayback = effectSession;

            effectSession.Start(
                async token =>
                {
                    using var process = CreateFfmpegProcess(track.FilePath, null);
                    effectSession.AttachProcess(process);

                    await using var opusStream = new OpusEncodeStream(
                        voiceClient.CreateVoiceStream(),
                        PcmFormat.Short,
                        VoiceChannels.Stereo,
                        OpusApplication.Audio);

                    await process.StandardOutput.BaseStream.CopyToAsync(opusStream, token).ConfigureAwait(false);
                    await opusStream.FlushAsync(token).ConfigureAwait(false);
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                },
                completedNaturally =>
                {
                    if (_effectPlayback == effectSession)
                    {
                        _effectPlayback = null;
                    }
                    _ = RaiseSoundEffectFinishedAsync();
                },
                exception => _logger.LogWarning(exception, "Discord bot {Label} failed to play sound effect {Track}", Label, track.Name));
        }
        finally
        {
            _voiceGate.Release();
        }
    }

    private Task RaiseSoundEffectFinishedAsync()
    {
        var handler = SoundEffectFinished;
        if (handler is null)
        {
            return Task.CompletedTask;
        }
        try
        {
            var tasks = handler.GetInvocationList()
                .OfType<Func<Task>>()
                .Select(h => h());
            return Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord bot {Label} failed to raise SoundEffectFinished", Label);
            return Task.CompletedTask;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _voiceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopPlaybackInternalAsync().ConfigureAwait(false);
            _state = _state with { PlaybackState = BotPlaybackState.Stopped };
        }
        finally
        {
            _voiceGate.Release();
        }
    }

    public async Task RegisterCommandsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync().ConfigureAwait(false);
        var applicationId = _client.Id;
        var guildId = ResolveCommandGuildId();
        await _commandService.RegisterCommandsAsync(_client.Rest, applicationId, guildId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _voiceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopPlaybackInternalAsync().ConfigureAwait(false);
            await DisconnectInternalAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _voiceGate.Release();
        }

        if (_startTask is not null)
        {
            try
            {
                await _client.CloseAsync(WebSocketCloseStatus.NormalClosure).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discord bot {Label} failed to close gateway connection", Label);
            }
        }
    }

    private async Task EnsureStartedAsync()
    {
        if (_startTask is not null)
        {
            await _startTask.ConfigureAwait(false);
            return;
        }

        await _startGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_startTask is null)
            {
                _startTask = StartInternalAsync();
            }
        }
        finally
        {
            _startGate.Release();
        }

        await _startTask.ConfigureAwait(false);
    }

    private async Task StartInternalAsync()
    {
        _logger.LogInformation("Starting Discord gateway for {Label}", Label);
        await _client.StartAsync().ConfigureAwait(false);
    }

    private void RegisterSlashCommands()
    {
        _commandService.AddSlashCommand(
            CreateGuildCommand(
                "join",
                "Connect bot to your voice channel",
                new Func<ApplicationCommandContext, Task<string>>(JoinAsync)));

        _commandService.AddSlashCommand(
            CreateGuildCommand(
                "leave",
                "Disconnect bot from voice channel",
                new Func<ApplicationCommandContext, Task<string>>(LeaveAsync)));

        _commandService.AddSlashCommand(
            CreateGuildCommand(
                "play",
                "Play track from playlist",
                new Func<ApplicationCommandContext, string, string?, Task<string>>(PlayFromCommandAsync)));

        _commandService.AddSlashCommand(
            CreateGuildCommand(
                "stop",
                "Stop playback",
                new Func<ApplicationCommandContext, Task<string>>(context =>
                    RaiseCommandAsync(context, new AudioBotCommand(BotCommandType.Stop), "Stopped."))));

        _commandService.AddSlashCommand(
            CreateGuildCommand(
                "next",
                "Next track",
                new Func<ApplicationCommandContext, Task<string>>(context =>
                    RaiseCommandAsync(context, new AudioBotCommand(BotCommandType.Next), "Next track."))));

        _commandService.AddSlashCommand(
            CreateGuildCommand(
                "previous",
                "Previous track",
                new Func<ApplicationCommandContext, Task<string>>(context =>
                    RaiseCommandAsync(context, new AudioBotCommand(BotCommandType.Previous), "Previous track."))));

        _commandService.AddSlashCommand(
            CreateGuildCommand(
                "effect",
                "Play sound effect over current playback",
                new Func<ApplicationCommandContext, string, string?, Task<string>>(EffectCommandAsync)));
    }

    private static SlashCommandBuilder CreateGuildCommand(string name, string description, Delegate handler)
    {
        var builder = new SlashCommandBuilder(name, description, handler)
        {
            Contexts = new[] { InteractionContextType.Guild }
        };
        return builder;
    }

    private Task<string> JoinAsync(ApplicationCommandContext context)
    {
        if (!TryGetUserVoiceChannelId(context, out var channelId, out var error))
        {
            return Task.FromResult(error);
        }

        return RaiseCommandAsync(context, new AudioBotCommand(BotCommandType.Connect, ChannelId: channelId), "Connecting...");
    }

    private Task<string> LeaveAsync(ApplicationCommandContext context)
        => RaiseCommandAsync(context, new AudioBotCommand(BotCommandType.Disconnect), "Disconnecting...");

    private Task<string> PlayFromCommandAsync(ApplicationCommandContext context, string playlistId, string? trackId = null)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            return Task.FromResult("PlaylistId is required.");
        }

        return RaiseCommandAsync(
            context,
            new AudioBotCommand(BotCommandType.Play, playlistId.Trim(), trackId),
            $"Playing playlist {playlistId}.");
    }

    private Task<string> EffectCommandAsync(ApplicationCommandContext context, string playlistId, string? trackId = null)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            return Task.FromResult("PlaylistId is required.");
        }

        return RaiseCommandAsync(
            context,
            new AudioBotCommand(BotCommandType.PlaySoundEffect, playlistId.Trim(), trackId),
            "Playing sound effect.");
    }

    private async Task<string> RaiseCommandAsync(ApplicationCommandContext context, AudioBotCommand command, string successMessage)
    {
        var handler = CommandReceived;
        if (handler is null)
        {
            return "Command handler is not configured.";
        }

        try
        {
            var tasks = handler.GetInvocationList()
                .OfType<Func<AudioBotCommand, Task>>()
                .Select(h => h(command));
            await Task.WhenAll(tasks).ConfigureAwait(false);
            return successMessage;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord bot {Label} failed to process command {CommandType}", Label, command.Type);
            return "Command failed.";
        }
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
            _logger.LogWarning(ex, "Discord bot {Label} failed to broadcast command {CommandType}", Label, command.Type);
            return Task.CompletedTask;
        }
    }

    private static bool TryGetUserVoiceChannelId(ApplicationCommandContext context, out string channelId, out string error)
    {
        channelId = string.Empty;
        error = string.Empty;

        var guild = context.Guild;
        if (guild is null)
        {
            error = "This command is only available in guilds.";
            return false;
        }

        if (!guild.VoiceStates.TryGetValue(context.User.Id, out var voiceState) || voiceState.ChannelId is null)
        {
            error = "You are not connected to a voice channel.";
            return false;
        }

        channelId = voiceState.ChannelId.Value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private async ValueTask HandleInteractionCreateAsync(Interaction interaction)
    {
        if (interaction is not ApplicationCommandInteraction commandInteraction)
        {
            return;
        }

        var context = new ApplicationCommandContext(commandInteraction, _client);
        var result = await _commandService.ExecuteAsync(context).ConfigureAwait(false);

        if (result is IFailResult failResult)
        {
            var message = string.IsNullOrWhiteSpace(failResult.Message)
                ? "Failed to execute command."
                : failResult.Message;

            await commandInteraction.SendResponseAsync(InteractionCallback.Message(message)).ConfigureAwait(false);
        }
    }

    private async Task<IVoiceGuildChannel> ResolveVoiceChannelAsync(string channelId, CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(channelId, NumberStyles.None, CultureInfo.InvariantCulture, out var channelKey))
        {
            throw new ArgumentException("ChannelId must be a numeric Discord channel id.", nameof(channelId));
        }

        var channel = await _client.Rest.GetChannelAsync(channelKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (channel is not IVoiceGuildChannel voiceChannel)
        {
            throw new InvalidOperationException("Channel is not a voice channel.");
        }

        return voiceChannel;
    }

    private ulong? ResolveGuildId()
    {
        if (TryGetSettingUlong(GuildIdSetting, out var guildId))
        {
            return guildId;
        }

        return _connectedGuildId;
    }

    private ulong? ResolveCommandGuildId()
    {
        if (TryGetSettingUlong(CommandGuildIdSetting, out var commandGuildId))
        {
            return commandGuildId;
        }

        return ResolveGuildId();
    }

    private bool TryGetSettingUlong(string key, out ulong value)
    {
        value = 0;
        if (!_configuration.Settings.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private Process CreateFfmpegProcess(string inputPath, double? startPositionSeconds = null)
    {
        var ffmpegPath = _configuration.Settings.TryGetValue(FfmpegPathSetting, out var configuredPath)
            && !string.IsNullOrWhiteSpace(configuredPath)
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
        if (startPositionSeconds.HasValue)
        {
            arguments.Add("-ss");
            arguments.Add(startPositionSeconds.Value.ToString("R", CultureInfo.InvariantCulture));
        }
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

    private async Task StopPlaybackInternalAsync()
    {
        _mainPlaybackStartPositionSeconds = 0;
        var playback = _playback;
        _playback = null;
        if (playback is not null)
        {
            await playback.StopAsync().ConfigureAwait(false);
        }

        var effectPlayback = _effectPlayback;
        _effectPlayback = null;
        if (effectPlayback is not null)
        {
            await effectPlayback.StopAsync().ConfigureAwait(false);
        }
    }

    private async Task DisconnectInternalAsync(CancellationToken cancellationToken)
    {
        var voiceClient = _voiceClient;
        if (voiceClient is null)
        {
            return;
        }

        _voiceClient = null;

        if (_connectedGuildId.HasValue)
        {
            await _client.UpdateVoiceStateAsync(new VoiceStateProperties(_connectedGuildId.Value, null), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            await voiceClient.CloseAsync(WebSocketCloseStatus.NormalClosure, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            voiceClient.Dispose();
        }

        _connectedGuildId = null;
        _connectedChannelId = null;
        _connectedChannelName = null;
    }

    private sealed class PlaybackSession
    {
        private const double BytesPerSecondPcm = 48000 * 2 * 2; // 48kHz stereo s16le

        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _task;
        private Process? _process;
        private int _stopRequested;
        private long _bytesRead;

        public PlaybackSession(AudioTrackInfo track)
        {
        }

        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        public double PositionSeconds => Interlocked.Read(ref _bytesRead) / BytesPerSecondPcm;

        public void AddBytesRead(int count)
        {
            if (count > 0)
            {
                Interlocked.Add(ref _bytesRead, count);
            }
        }

        public void AttachProcess(Process process)
        {
            _process = process;
        }

        public void Start(Func<CancellationToken, Task> work, Action<bool> onCompleted, Action<Exception> onError)
        {
            _task = Task.Run(async () =>
            {
                var completedSuccessfully = false;
                try
                {
                    await work(CancellationToken).ConfigureAwait(false);
                    completedSuccessfully = true;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    onError(ex);
                }
                finally
                {
                    var stopped = _stopRequested == 1 || CancellationToken.IsCancellationRequested;
                    var completedNaturally = completedSuccessfully && !stopped;
                    CleanupProcess();
                    onCompleted(completedNaturally);
                }
            });
        }

        public async Task StopAsync()
        {
            Interlocked.Exchange(ref _stopRequested, 1);
            _cancellationTokenSource.Cancel();
            TryKillAttachedProcess();

            if (_task is not null)
            {
                try
                {
                    await _task.ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        private void TryKillAttachedProcess()
        {
            var process = _process;
            if (process is null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
                // Process was never started, or already disposed on another thread.
            }
            catch
            {
            }
        }

        private void CleanupProcess()
        {
            if (_process is null)
            {
                return;
            }

            try
            {
                TryKillAttachedProcess();
            }
            finally
            {
                try
                {
                    _process?.Dispose();
                }
                catch (InvalidOperationException)
                {
                }

                _process = null;
            }
        }
    }
}
