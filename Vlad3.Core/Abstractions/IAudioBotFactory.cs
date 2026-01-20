using Vlad3.Core.Models;

namespace Vlad3.Core.Abstractions;

/// <summary>
/// Фабрика для создания экземпляров аудио-бота заданного типа.
/// </summary>
public interface IAudioBotFactory
{
    string Type { get; }
    string DisplayName { get; }

    /// <summary>
    /// Создать экземпляр бота по конфигурации.
    /// </summary>
    Task<IAudioBot> CreateAsync(BotConfiguration configuration, CancellationToken cancellationToken = default);
}
