namespace Vlad3.Core.Models;

/// <summary>
/// Трек внутри плейлиста с метаданными хранения.
/// </summary>
/// <param name="Id">Идентификатор трека.</param>
/// <param name="OriginalName">Исходное имя файла.</param>
/// <param name="StoredFileName">Имя файла в хранилище.</param>
/// <param name="Order">Порядковый номер в плейлисте.</param>
/// <param name="Size">Размер файла в байтах.</param>
/// <param name="ContentType">MIME-тип файла.</param>
public sealed record PlaylistTrack(
    string Id,
    string OriginalName,
    string StoredFileName,
    int Order,
    long Size,
    string ContentType);
