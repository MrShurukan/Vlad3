namespace Vlad3.Application.Playlists;

public sealed record UploadFile(string FileName, string ContentType, Stream Content, long Size);
