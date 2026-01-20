namespace Vlad3.Core.Models;

/// <summary>
/// Сводная информация о плейлисте.
/// </summary>
/// <param name="Id">Идентификатор плейлиста.</param>
/// <param name="Name">Название плейлиста.</param>
/// <param name="TrackCount">Количество треков.</param>
public sealed record PlaylistInfo(string Id, string Name, int TrackCount);
