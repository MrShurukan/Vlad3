namespace Vlad3.Core.Models;

public sealed record AudioBotCommand(
    BotCommandType Type,
    string? PlaylistId = null,
    string? TrackId = null,
    string? ChannelId = null);
