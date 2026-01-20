namespace Vlad3.Core.Models;

/// <summary>
/// Информация о треке для воспроизведения.
/// </summary>
/// <param name="Id">Идентификатор трека.</param>
/// <param name="Name">Отображаемое название трека.</param>
/// <param name="FilePath">Путь к локальному файлу.</param>
/// <param name="Order">Позиция трека в плейлисте.</param>
public sealed record AudioTrackInfo(string Id, string Name, string FilePath, int Order);
