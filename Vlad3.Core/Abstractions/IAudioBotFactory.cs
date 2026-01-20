using Vlad3.Core.Models;

namespace Vlad3.Core.Abstractions;

public interface IAudioBotFactory
{
    string Type { get; }
    string DisplayName { get; }

    Task<IAudioBot> CreateAsync(BotConfiguration configuration, CancellationToken cancellationToken = default);
}
