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
    /// <param name="track">Трек для воспроизведения.</param>
    /// <param name="startPositionSeconds">Позиция начала в секундах (для возобновления с места). Если задана, используется ffmpeg -ss перед -i.</param>
    Task PlayAsync(AudioTrackInfo track, double? startPositionSeconds = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Остановить воспроизведение.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Проиграть звуковой эффект поверх текущего воспроизведения. Если идёт воспроизведение — останавливается (без PlaybackFinished),
    /// перед остановкой вызывается <paramref name="onMainPlaybackPaused"/> с текущей позицией в секундах, затем проигрывается эффект.
    /// По окончании эффекта вызывается событие <see cref="SoundEffectFinished"/>.
    /// </summary>
    Task PlaySoundEffectAsync(AudioTrackInfo track, Action<double>? onMainPlaybackPaused = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Событие команд, пришедших со стороны платформы (чат/слэш-команды).
    /// </summary>
    event Func<AudioBotCommand, Task>? CommandReceived;

    /// <summary>
    /// Событие: звуковой эффект завершён; вызывающая сторона может возобновить основной трек через PlayAsync с позицией.
    /// </summary>
    event Func<Task>? SoundEffectFinished;

    /// <summary>
    /// Зарегистрировать команды, доступные в платформе.
    /// </summary>
    Task RegisterCommandsAsync(CancellationToken cancellationToken = default);
}
