namespace Vlad3.Core.Models;

public sealed record BotState(
    BotConnectionState ConnectionState,
    BotPlaybackState PlaybackState,
    string? ConnectedChannelId,
    string? ConnectedChannelName,
    string? CurrentPlaylistId,
    string? CurrentTrackId);
