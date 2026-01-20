using Vlad3.Core.Models;

namespace Vlad3.WebControl.Models;

public sealed record CreateBotRequest(
    string Type,
    string Label,
    string ApiKey,
    IReadOnlyDictionary<string, string>? Settings);

public sealed record UpdateBotRequest(
    string? Label,
    string? ApiKey,
    IReadOnlyDictionary<string, string>? Settings);

public sealed record BotDto(
    string Id,
    string Type,
    string Label,
    IReadOnlyDictionary<string, string> Settings,
    BotStateDto State);

public sealed record BotStateDto(
    BotConnectionState ConnectionState,
    BotPlaybackState PlaybackState,
    string? ConnectedChannelId,
    string? ConnectedChannelName,
    string? CurrentPlaylistId,
    string? CurrentTrackId,
    bool AutoNextEnabled);

public sealed record BotTypeDto(string Type, string DisplayName);

public sealed record ConnectBotRequest(string ChannelId);

public sealed record PlayBotRequest(string PlaylistId, string? TrackId);

public sealed record AutoNextStateDto(bool Enabled);
