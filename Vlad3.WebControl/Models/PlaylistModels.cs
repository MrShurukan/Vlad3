namespace Vlad3.WebControl.Models;

public sealed record CreatePlaylistRequest(string Name);

public sealed record PlaylistDto(string Id, string Name, int TrackCount);

public sealed record TrackDto(
    string Id,
    string Name,
    int Order,
    long Size,
    string ContentType);

public sealed record ReorderTracksRequest(IReadOnlyList<string> TrackIds);
