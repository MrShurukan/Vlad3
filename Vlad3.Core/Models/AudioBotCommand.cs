namespace Vlad3.Core.Models;

/// <summary>
/// Команда управления, пришедшая от платформы (чат/слэш-команда).
/// </summary>
/// <param name="Type">Тип команды.</param>
/// <param name="PlaylistId">Идентификатор плейлиста (если применимо).</param>
/// <param name="TrackId">Идентификатор трека (если применимо).</param>
/// <param name="ChannelId">Идентификатор канала (если применимо).</param>
public sealed record AudioBotCommand(
    BotCommandType Type,
    string? PlaylistId = null,
    string? TrackId = null,
    string? ChannelId = null);
