namespace Vlad3.Core.Models;

/// <summary>
/// Состояние подключения бота к аудио-каналу.
/// </summary>
public enum BotConnectionState
{
    /// <summary>
    /// Бот не подключен.
    /// </summary>
    Disconnected = 0,

    /// <summary>
    /// Бот подключен.
    /// </summary>
    Connected = 1
}

/// <summary>
/// Состояние воспроизведения.
/// </summary>
public enum BotPlaybackState
{
    /// <summary>
    /// Воспроизведение остановлено.
    /// </summary>
    Stopped = 0,

    /// <summary>
    /// Идет воспроизведение.
    /// </summary>
    Playing = 1
}

/// <summary>
/// Тип команды управления ботом.
/// </summary>
public enum BotCommandType
{
    /// <summary>
    /// Запуск воспроизведения.
    /// </summary>
    Play = 0,

    /// <summary>
    /// Остановка воспроизведения.
    /// </summary>
    Stop = 1,

    /// <summary>
    /// Следующий трек.
    /// </summary>
    Next = 2,

    /// <summary>
    /// Предыдущий трек.
    /// </summary>
    Previous = 3,

    /// <summary>
    /// Подключение к каналу.
    /// </summary>
    Connect = 4,

    /// <summary>
    /// Отключение от канала.
    /// </summary>
    Disconnect = 5,

    /// <summary>
    /// Изменить тип движения по плейлисту.
    /// </summary>
    ChangePlaylistMovementType = 6,

    /// <summary>
    /// Уведомление о завершении текущего трека.
    /// </summary>
    PlaybackFinished = 7
}

/// <summary>
/// Тип движения по плейлисту.
/// </summary>
public enum PlaylistMovementType
{
    /// <summary>
    /// После завершения трека бот останавливается.
    /// </summary>
    None = 0,
    /// <summary>
    /// После завершения трека бот переходит на следующий.
    /// </summary>
    AutoNext = 1,
    /// <summary>
    /// После завершения трека бот повторяет его ещё раз.
    /// </summary>
    RepeatCurrent = 2
}