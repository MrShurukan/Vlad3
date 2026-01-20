using Vlad3.Core.Models;

namespace Vlad3.Application.Bots;

public interface IBotConfigurationStore
{
    Task<IReadOnlyList<BotConfiguration>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<BotConfiguration?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task AddAsync(BotConfiguration configuration, CancellationToken cancellationToken = default);
    Task UpdateAsync(BotConfiguration configuration, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
