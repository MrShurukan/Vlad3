using Microsoft.Extensions.Logging;
using Vlad3.Core.Abstractions;
using Vlad3.Core.Models;

namespace Vlad3.Bots.TeamSpeak;

public sealed class TeamSpeakAudioBot : IAudioBot
{
    private readonly BotConfiguration _configuration;
    private readonly ILogger<TeamSpeakAudioBot> _logger;
    private readonly List<AudioChannelInfo> _channels;
    private BotState _state;

    public TeamSpeakAudioBot(BotConfiguration configuration, ILogger<TeamSpeakAudioBot> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _channels = BuildChannels(configuration);
        _state = new BotState(BotConnectionState.Disconnected, BotPlaybackState.Stopped, null, null, null, null);
    }

    public string Id => _configuration.Id;
    public string Type => _configuration.Type;
    public string Label => _configuration.Label;
    public BotState State => _state;

    public event Func<AudioBotCommand, Task>? CommandReceived;

    public Task<IReadOnlyList<AudioChannelInfo>> GetAvailableChannelsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AudioChannelInfo>>(_channels);

    public Task ConnectAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var channel = _channels.FirstOrDefault(item => item.Id == channelId);
        _state = _state with
        {
            ConnectionState = BotConnectionState.Connected,
            ConnectedChannelId = channel?.Id ?? channelId,
            ConnectedChannelName = channel?.Name ?? channelId
        };
        _logger.LogInformation("TeamSpeak bot {Label} connected to channel {Channel}", Label, channelId);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _state = _state with
        {
            ConnectionState = BotConnectionState.Disconnected,
            ConnectedChannelId = null,
            ConnectedChannelName = null,
            PlaybackState = BotPlaybackState.Stopped
        };
        _logger.LogInformation("TeamSpeak bot {Label} disconnected", Label);
        return Task.CompletedTask;
    }

    public Task PlayAsync(AudioTrackInfo track, CancellationToken cancellationToken = default)
    {
        _state = _state with { PlaybackState = BotPlaybackState.Playing };
        _logger.LogInformation("TeamSpeak bot {Label} playing {Track}", Label, track.Name);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _state = _state with { PlaybackState = BotPlaybackState.Stopped };
        _logger.LogInformation("TeamSpeak bot {Label} stopped playback", Label);
        return Task.CompletedTask;
    }

    public Task RegisterCommandsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("TeamSpeak bot {Label} registered commands", Label);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _logger.LogInformation("TeamSpeak bot {Label} disposed", Label);
        return ValueTask.CompletedTask;
    }

    private static List<AudioChannelInfo> BuildChannels(BotConfiguration configuration)
    {
        if (configuration.Settings.TryGetValue("channels", out var rawChannels)
            && !string.IsNullOrWhiteSpace(rawChannels))
        {
            var channels = rawChannels
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((name, index) => new AudioChannelInfo($"ts-{index + 1}", name))
                .ToList();

            if (channels.Count > 0)
            {
                return channels;
            }
        }

        return new List<AudioChannelInfo>
        {
            new("ts-1", "TeamSpeak Lobby"),
            new("ts-2", "TeamSpeak Music")
        };
    }
}
