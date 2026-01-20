using Vlad3.Core.Models;

namespace Vlad3.Core.Abstractions;

/// <summary>
/// Базовый контракт аудио-бота конкретной платформы.
/// </summary>
public interface IAudioBot : IAsyncDisposable
{
    string Id { get; }
    string Type { get; }
    string Label { get; }

    BotState State { get; }

    /// <summary>
    /// Получить доступные аудио-каналы платформы.
    /// </summary>
    Task<IReadOnlyList<AudioChannelInfo>> GetAvailableChannelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Подключиться к аудио-каналу по идентификатору.
    /// </summary>
    Task ConnectAsync(string channelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отключиться от текущего канала.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Начать воспроизведение конкретного трека.
    /// </summary>
    Task PlayAsync(AudioTrackInfo track, CancellationToken cancellationToken = default);

    /// <summary>
    /// Остановить воспроизведение.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Событие команд, пришедших со стороны платформы (чат/слэш-команды).
    /// </summary>
    event Func<AudioBotCommand, Task>? CommandReceived;

    /// <summary>
    /// Зарегистрировать команды, доступные в платформе.
    /// </summary>
    Task RegisterCommandsAsync(CancellationToken cancellationToken = default);
}
