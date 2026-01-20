using Microsoft.Extensions.Logging;
using Vlad3.Application.Errors;
using Vlad3.Application.Playlists;
using Vlad3.Core.Abstractions;
using Vlad3.Core.Models;

namespace Vlad3.Application.Bots;

public sealed class BotManager : IBotManager
{
    private readonly IBotConfigurationStore _configurationStore;
    private readonly BotFactoryRegistry _factoryRegistry;
    private readonly IPlaylistService _playlistService;
    private readonly ILogger<BotManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, BotRuntime> _bots = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public BotManager(
        IBotConfigurationStore configurationStore,
        BotFactoryRegistry factoryRegistry,
        IPlaylistService playlistService,
        ILogger<BotManager> logger)
    {
        _configurationStore = configurationStore;
        _factoryRegistry = factoryRegistry;
        _playlistService = playlistService;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var configs = await _configurationStore.GetAllAsync(cancellationToken);
            foreach (var config in configs)
            {
                var runtime = await CreateRuntimeAsync(config, cancellationToken);
                _bots[config.Id] = runtime;
            }

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<BotTypeInfo>> GetBotTypesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_factoryRegistry.GetTypes());

    public async Task<IReadOnlyList<BotSummary>> GetBotsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _bots.Values.Select(BuildSummary).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BotSummary> GetBotAsync(string botId, CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeAsync(botId, cancellationToken);
        return BuildSummary(runtime);
    }

    public async Task<BotSummary> CreateBotAsync(
        string type,
        string label,
        string apiKey,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(label))
        {
            throw ServiceException.BadRequest("Bot type and label are required.");
        }

        var settingsCopy = settings is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(settings);

        var config = new BotConfiguration(
            Guid.NewGuid().ToString("N"),
            type.Trim(),
            label.Trim(),
            apiKey ?? string.Empty,
            settingsCopy);

        var runtime = await CreateRuntimeAsync(config, cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _bots[config.Id] = runtime;
            await _configurationStore.AddAsync(config, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return BuildSummary(runtime);
    }

    public async Task<BotSummary> UpdateBotAsync(
        string botId,
        string? label,
        string? apiKey,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default)
    {
        BotRuntime runtime;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            runtime = GetRuntime(botId);
        }
        finally
        {
            _gate.Release();
        }

        var settingsCopy = settings is null
            ? runtime.Configuration.Settings
            : new Dictionary<string, string>(settings);

        var updatedConfig = runtime.Configuration with
        {
            Label = string.IsNullOrWhiteSpace(label) ? runtime.Configuration.Label : label.Trim(),
            ApiKey = apiKey ?? runtime.Configuration.ApiKey,
            Settings = settingsCopy
        };

        var replacement = await CreateRuntimeAsync(updatedConfig, cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await runtime.Bot.DisposeAsync();
            _bots[botId] = replacement;
            await _configurationStore.UpdateAsync(updatedConfig, cancellationToken);
            return BuildSummary(replacement);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteBotAsync(string botId, CancellationToken cancellationToken = default)
    {
        BotRuntime runtime;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            runtime = GetRuntime(botId);
            _bots.Remove(botId);
            await _configurationStore.DeleteAsync(botId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        await runtime.Bot.DisposeAsync();
    }

    public async Task<IReadOnlyList<AudioChannelInfo>> GetChannelsAsync(string botId, CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeAsync(botId, cancellationToken);
        return await runtime.Bot.GetAvailableChannelsAsync(cancellationToken);
    }

    public async Task ConnectAsync(string botId, string channelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw ServiceException.BadRequest("ChannelId is required.");
        }

        var runtime = await GetRuntimeAsync(botId, cancellationToken);
        var channels = await runtime.Bot.GetAvailableChannelsAsync(cancellationToken);
        var channel = channels.FirstOrDefault(item => item.Id == channelId);
        if (channel is null)
        {
            throw ServiceException.NotFound("Channel not found.");
        }

        if (runtime.Bot.State.ConnectionState == BotConnectionState.Connected
            && runtime.Bot.State.ConnectedChannelId == channelId)
        {
            return;
        }

        if (runtime.Bot.State.ConnectionState == BotConnectionState.Connected)
        {
            await runtime.Bot.DisconnectAsync(cancellationToken);
        }

        await runtime.Bot.ConnectAsync(channelId, cancellationToken);
    }

    public async Task DisconnectAsync(string botId, CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeAsync(botId, cancellationToken);
        if (runtime.Bot.State.ConnectionState == BotConnectionState.Disconnected)
        {
            return;
        }

        await runtime.Bot.DisconnectAsync(cancellationToken);
    }

    public async Task PlayAsync(
        string botId,
        string playlistId,
        string? trackId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            throw ServiceException.BadRequest("PlaylistId is required.");
        }

        var runtime = await GetRuntimeAsync(botId, cancellationToken);
        if (runtime.Bot.State.ConnectionState == BotConnectionState.Disconnected)
        {
            throw ServiceException.Conflict("Bot is not connected to a channel.");
        }

        var resolvedTrackId = await ResolveTrackIdAsync(runtime, playlistId, trackId, cancellationToken);
        var trackInfo = await _playlistService.GetTrackFileAsync(playlistId, resolvedTrackId, cancellationToken);

        await UpdatePlaybackContextAsync(runtime, playlistId, trackInfo.Id, cancellationToken);
        await runtime.Bot.PlayAsync(trackInfo, cancellationToken);
    }

    public async Task StopAsync(string botId, CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeAsync(botId, cancellationToken);
        await runtime.Bot.StopAsync(cancellationToken);
    }

    public async Task NextAsync(string botId, CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeAsync(botId, cancellationToken);
        var playlistId = runtime.Playback.PlaylistId;
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            throw ServiceException.BadRequest("Playlist is not selected.");
        }

        if (runtime.Bot.State.ConnectionState == BotConnectionState.Disconnected)
        {
            throw ServiceException.Conflict("Bot is not connected to a channel.");
        }

        var tracks = await _playlistService.GetTracksAsync(playlistId, cancellationToken);
        if (tracks.Count == 0)
        {
            throw ServiceException.Conflict("Playlist is empty.");
        }

        var currentIndex = ResolveCurrentIndex(tracks, runtime.Playback.TrackId);
        var nextIndex = (currentIndex + 1) % tracks.Count;
        var nextTrackId = tracks[nextIndex].Id;
        var trackInfo = await _playlistService.GetTrackFileAsync(playlistId, nextTrackId, cancellationToken);

        await UpdatePlaybackContextAsync(runtime, playlistId, trackInfo.Id, cancellationToken);
        await runtime.Bot.PlayAsync(trackInfo, cancellationToken);
    }

    public async Task PreviousAsync(string botId, CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeAsync(botId, cancellationToken);
        var playlistId = runtime.Playback.PlaylistId;
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            throw ServiceException.BadRequest("Playlist is not selected.");
        }

        if (runtime.Bot.State.ConnectionState == BotConnectionState.Disconnected)
        {
            throw ServiceException.Conflict("Bot is not connected to a channel.");
        }

        var tracks = await _playlistService.GetTracksAsync(playlistId, cancellationToken);
        if (tracks.Count == 0)
        {
            throw ServiceException.Conflict("Playlist is empty.");
        }

        var currentIndex = ResolveCurrentIndex(tracks, runtime.Playback.TrackId);
        var previousIndex = (currentIndex - 1 + tracks.Count) % tracks.Count;
        var previousTrackId = tracks[previousIndex].Id;
        var trackInfo = await _playlistService.GetTrackFileAsync(playlistId, previousTrackId, cancellationToken);

        await UpdatePlaybackContextAsync(runtime, playlistId, trackInfo.Id, cancellationToken);
        await runtime.Bot.PlayAsync(trackInfo, cancellationToken);
    }

    private async Task<BotRuntime> GetRuntimeAsync(string botId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return GetRuntime(botId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private BotRuntime GetRuntime(string botId)
    {
        if (!_bots.TryGetValue(botId, out var runtime))
        {
            throw ServiceException.NotFound("Bot not found.");
        }

        return runtime;
    }

    private BotSummary BuildSummary(BotRuntime runtime)
    {
        var state = runtime.Bot.State;
        var enrichedState = new BotState(
            state.ConnectionState,
            state.PlaybackState,
            state.ConnectedChannelId,
            state.ConnectedChannelName,
            runtime.Playback.PlaylistId,
            runtime.Playback.TrackId);

        return new BotSummary(runtime.Configuration, enrichedState);
    }

    private async Task<BotRuntime> CreateRuntimeAsync(BotConfiguration config, CancellationToken cancellationToken)
    {
        var factory = _factoryRegistry.GetFactory(config.Type);
        var bot = await factory.CreateAsync(config, cancellationToken);
        bot.CommandReceived += command => HandleBotCommandAsync(bot.Id, command);
        await bot.RegisterCommandsAsync(cancellationToken);
        return new BotRuntime(bot, config);
    }

    private async Task HandleBotCommandAsync(string botId, AudioBotCommand command)
    {
        try
        {
            switch (command.Type)
            {
                case BotCommandType.Play:
                    await PlayAsync(botId, command.PlaylistId ?? string.Empty, command.TrackId, CancellationToken.None);
                    break;
                case BotCommandType.Stop:
                    await StopAsync(botId, CancellationToken.None);
                    break;
                case BotCommandType.Next:
                    await NextAsync(botId, CancellationToken.None);
                    break;
                case BotCommandType.Previous:
                    await PreviousAsync(botId, CancellationToken.None);
                    break;
                case BotCommandType.Connect:
                    if (!string.IsNullOrWhiteSpace(command.ChannelId))
                    {
                        await ConnectAsync(botId, command.ChannelId, CancellationToken.None);
                    }
                    break;
                case BotCommandType.Disconnect:
                    await DisconnectAsync(botId, CancellationToken.None);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle bot command {CommandType} for bot {BotId}", command.Type, botId);
        }
    }

    private async Task<string> ResolveTrackIdAsync(
        BotRuntime runtime,
        string playlistId,
        string? requestedTrackId,
        CancellationToken cancellationToken)
    {
        var tracks = await _playlistService.GetTracksAsync(playlistId, cancellationToken);
        if (tracks.Count == 0)
        {
            throw ServiceException.Conflict("Playlist is empty.");
        }

        if (!string.IsNullOrWhiteSpace(requestedTrackId))
        {
            if (tracks.All(track => track.Id != requestedTrackId))
            {
                throw ServiceException.NotFound("Requested track not found in playlist.");
            }

            return requestedTrackId;
        }

        if (runtime.Playback.PlaylistId == playlistId && !string.IsNullOrWhiteSpace(runtime.Playback.TrackId))
        {
            for (var index = 0; index < tracks.Count; index++)
            {
                if (tracks[index].Id == runtime.Playback.TrackId)
                {
                    return runtime.Playback.TrackId!;
                }
            }
        }

        return tracks[0].Id;
    }

    private async Task UpdatePlaybackContextAsync(
        BotRuntime runtime,
        string playlistId,
        string trackId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            runtime.Playback.PlaylistId = playlistId;
            runtime.Playback.TrackId = trackId;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static int ResolveCurrentIndex(IReadOnlyList<PlaylistTrack> tracks, string? currentTrackId)
    {
        if (string.IsNullOrWhiteSpace(currentTrackId))
        {
            return 0;
        }

        for (var index = 0; index < tracks.Count; index++)
        {
            if (tracks[index].Id == currentTrackId)
            {
                return index;
            }
        }

        return 0;
    }

    private sealed class BotRuntime
    {
        public BotRuntime(IAudioBot bot, BotConfiguration configuration)
        {
            Bot = bot;
            Configuration = configuration;
        }

        public IAudioBot Bot { get; }
        public BotConfiguration Configuration { get; }
        public BotPlaybackContext Playback { get; } = new();
    }

    private sealed class BotPlaybackContext
    {
        public string? PlaylistId { get; set; }
        public string? TrackId { get; set; }
    }
}
