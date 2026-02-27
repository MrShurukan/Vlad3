using Vlad3.Core.Models;

namespace Vlad3.Application.Bots;

public interface IBotManager
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BotTypeInfo>> GetBotTypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BotSummary>> GetBotsAsync(CancellationToken cancellationToken = default);
    Task<BotSummary> GetBotAsync(string botId, CancellationToken cancellationToken = default);
    Task<BotSummary> CreateBotAsync(
        string type,
        string label,
        string apiKey,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default);
    Task<BotSummary> UpdateBotAsync(
        string botId,
        string? label,
        string? apiKey,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default);
    Task DeleteBotAsync(string botId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AudioChannelInfo>> GetChannelsAsync(string botId, CancellationToken cancellationToken = default);
    Task ConnectAsync(string botId, string channelId, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string botId, CancellationToken cancellationToken = default);
    Task PlayAsync(
        string botId,
        string playlistId,
        string? trackId,
        double? startPositionSeconds = null,
        CancellationToken cancellationToken = default);
    Task StopAsync(string botId, CancellationToken cancellationToken = default);
    Task NextAsync(string botId, CancellationToken cancellationToken = default);
    Task PreviousAsync(string botId, CancellationToken cancellationToken = default);
    Task PlaySoundEffectAsync(string botId, string playlistId, string? trackId, CancellationToken cancellationToken = default);
    Task<PlaylistMovementType> ChangePlaylistMovementType(string botId, PlaylistMovementType type, CancellationToken cancellationToken = default);
}
