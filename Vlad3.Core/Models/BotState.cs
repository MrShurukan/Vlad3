namespace Vlad3.Core.Models;

/// <summary>
/// Текущее состояние бота для отображения в UI и логике управления.
/// </summary>
/// <param name="ConnectionState">Состояние подключения к каналу.</param>
/// <param name="PlaybackState">Состояние воспроизведения.</param>
/// <param name="ConnectedChannelId">Идентификатор подключенного канала.</param>
/// <param name="ConnectedChannelName">Название подключенного канала.</param>
/// <param name="CurrentPlaylistId">Текущий плейлист.</param>
/// <param name="CurrentTrackId">Текущий трек.</param>
/// <param name="PlaylistMovementType">Тип движения по плейлисту.</param>
public sealed record BotState(
    BotConnectionState ConnectionState,
    BotPlaybackState PlaybackState,
    string? ConnectedChannelId,
    string? ConnectedChannelName,
    string? CurrentPlaylistId,
    string? CurrentTrackId,
    PlaylistMovementType PlaylistMovementType);
