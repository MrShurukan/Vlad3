namespace Vlad3.Core.Models;

public sealed record PlaylistTrack(
    string Id,
    string OriginalName,
    string StoredFileName,
    int Order,
    long Size,
    string ContentType);
