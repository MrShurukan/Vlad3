using System.Text.Json;
using Vlad3.Application.Errors;
using Vlad3.Application.Storage;
using Vlad3.Core.Models;

namespace Vlad3.Application.Playlists;

public sealed class PlaylistService : IPlaylistService
{
    private readonly StoragePathResolver _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public PlaylistService(StoragePathResolver paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<PlaylistInfo>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsurePlaylistsRoot();
            var playlists = new List<PlaylistInfo>();
            foreach (var folder in Directory.EnumerateDirectories(_paths.PlaylistsRoot))
            {
                var playlistId = Path.GetFileName(folder);
                var data = await ReadPlaylistAsync(playlistId, cancellationToken, allowMissing: true);
                if (data is null)
                {
                    continue;
                }

                playlists.Add(new PlaylistInfo(data.Id, data.Name, data.Tracks.Count));
            }

            return playlists;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PlaylistInfo> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw ServiceException.BadRequest("Playlist name is required.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsurePlaylistsRoot();
            var id = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(_paths.GetPlaylistFolder(id));
            Directory.CreateDirectory(_paths.GetPlaylistFilesFolder(id));

            var data = new PlaylistData
            {
                Id = id,
                Name = name.Trim(),
                Tracks = new List<PlaylistTrackData>()
            };

            await WritePlaylistAsync(id, data, cancellationToken);
            return new PlaylistInfo(data.Id, data.Name, 0);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeletePlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var folder = _paths.GetPlaylistFolder(playlistId);
            if (!Directory.Exists(folder))
            {
                throw ServiceException.NotFound("Playlist not found.");
            }

            Directory.Delete(folder, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PlaylistTrack>> GetTracksAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var playlist = await ReadPlaylistAsync(playlistId, cancellationToken, allowMissing: false);
            return playlist!.Tracks
                .OrderBy(track => track.Order)
                .Select(MapTrack)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PlaylistTrack>> AddTracksAsync(
        string playlistId,
        IReadOnlyList<UploadFile> files,
        TrackInsertPosition? insertPosition,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            throw ServiceException.BadRequest("At least one file is required.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var playlist = await ReadPlaylistAsync(playlistId, cancellationToken, allowMissing: false);
            var tracks = playlist!.Tracks;
            var insertIndex = ResolveInsertIndex(tracks, insertPosition);
            var filesFolder = _paths.GetPlaylistFilesFolder(playlistId);
            Directory.CreateDirectory(filesFolder);

            var inserted = new List<PlaylistTrack>();
            foreach (var file in files)
            {
                var trackId = Guid.NewGuid().ToString("N");
                var safeName = SanitizeFileName(file.FileName);
                var storedFileName = $"{trackId}_{safeName}";
                var filePath = Path.Combine(filesFolder, storedFileName);

                await using (file.Content)
                await using (var output = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await file.Content.CopyToAsync(output, cancellationToken);
                }

                var trackData = new PlaylistTrackData
                {
                    Id = trackId,
                    OriginalName = safeName,
                    StoredFileName = storedFileName,
                    Size = file.Size,
                    ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType
                };

                tracks.Insert(insertIndex, trackData);
                insertIndex++;
                inserted.Add(MapTrack(trackData));
            }

            Reindex(tracks);
            await WritePlaylistAsync(playlistId, playlist, cancellationToken);
            return inserted
                .Select(track => track with { Order = tracks.First(t => t.Id == track.Id).Order })
                .OrderBy(track => track.Order)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteTrackAsync(string playlistId, string trackId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var playlist = await ReadPlaylistAsync(playlistId, cancellationToken, allowMissing: false);
            var tracks = playlist!.Tracks;
            var track = tracks.FirstOrDefault(existing => existing.Id == trackId);
            if (track is null)
            {
                throw ServiceException.NotFound("Track not found.");
            }

            var filesFolder = _paths.GetPlaylistFilesFolder(playlistId);
            var filePath = Path.Combine(filesFolder, track.StoredFileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            tracks.Remove(track);
            Reindex(tracks);
            await WritePlaylistAsync(playlistId, playlist, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReorderTracksAsync(
        string playlistId,
        IReadOnlyList<string> orderedTrackIds,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var playlist = await ReadPlaylistAsync(playlistId, cancellationToken, allowMissing: false);
            var tracks = playlist!.Tracks;

            if (orderedTrackIds.Count != tracks.Count)
            {
                throw ServiceException.BadRequest("Track list does not match existing tracks.");
            }

            if (orderedTrackIds.Distinct(StringComparer.Ordinal).Count() != tracks.Count)
            {
                throw ServiceException.BadRequest("Track list contains duplicate IDs.");
            }

            var trackLookup = tracks.ToDictionary(track => track.Id, track => track);
            var newOrder = new List<PlaylistTrackData>();
            foreach (var trackId in orderedTrackIds)
            {
                if (!trackLookup.TryGetValue(trackId, out var track))
                {
                    throw ServiceException.BadRequest("Track list contains unknown track IDs.");
                }

                newOrder.Add(track);
            }

            playlist.Tracks = newOrder;
            Reindex(playlist.Tracks);
            await WritePlaylistAsync(playlistId, playlist, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AudioTrackInfo> GetTrackFileAsync(string playlistId, string trackId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var playlist = await ReadPlaylistAsync(playlistId, cancellationToken, allowMissing: false);
            var track = playlist!.Tracks.FirstOrDefault(existing => existing.Id == trackId);
            if (track is null)
            {
                throw ServiceException.NotFound("Track not found.");
            }

            var filePath = Path.Combine(_paths.GetPlaylistFilesFolder(playlistId), track.StoredFileName);
            return new AudioTrackInfo(track.Id, track.OriginalName, filePath, track.Order);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsurePlaylistsRoot()
    {
        Directory.CreateDirectory(_paths.PlaylistsRoot);
    }

    private async Task<PlaylistData?> ReadPlaylistAsync(
        string playlistId,
        CancellationToken cancellationToken,
        bool allowMissing)
    {
        EnsurePlaylistsRoot();
        var playlistFile = _paths.GetPlaylistFile(playlistId);
        if (!File.Exists(playlistFile))
        {
            if (allowMissing)
            {
                return null;
            }

            throw ServiceException.NotFound("Playlist not found.");
        }

        var json = await File.ReadAllTextAsync(playlistFile, cancellationToken);
        var data = JsonSerializer.Deserialize<PlaylistData>(json, _jsonOptions);
        if (data is null)
        {
            throw ServiceException.NotFound("Playlist data is missing.");
        }

        return data;
    }

    private async Task WritePlaylistAsync(string playlistId, PlaylistData data, CancellationToken cancellationToken)
    {
        var playlistFile = _paths.GetPlaylistFile(playlistId);
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        await File.WriteAllTextAsync(playlistFile, json, cancellationToken);
    }

    private static PlaylistTrack MapTrack(PlaylistTrackData track)
        => new(track.Id, track.OriginalName, track.StoredFileName, track.Order, track.Size, track.ContentType);

    private static int ResolveInsertIndex(List<PlaylistTrackData> tracks, TrackInsertPosition? insertPosition)
    {
        if (insertPosition is null)
        {
            return tracks.Count;
        }

        if (!string.IsNullOrWhiteSpace(insertPosition.AfterTrackId))
        {
            var index = tracks.FindIndex(track => track.Id == insertPosition.AfterTrackId);
            if (index < 0)
            {
                throw ServiceException.NotFound("Track to insert after was not found.");
            }

            return index + 1;
        }

        if (insertPosition.AfterOrder.HasValue)
        {
            var order = insertPosition.AfterOrder.Value;
            if (order < 0 || order > tracks.Count)
            {
                throw ServiceException.BadRequest("Insert position is out of range.");
            }

            return order;
        }

        return tracks.Count;
    }

    private static void Reindex(List<PlaylistTrackData> tracks)
    {
        for (var index = 0; index < tracks.Count; index++)
        {
            tracks[index].Order = index + 1;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "track" : name;
    }

    private sealed class PlaylistData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<PlaylistTrackData> Tracks { get; set; } = new();
    }

    private sealed class PlaylistTrackData
    {
        public string Id { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public string StoredFileName { get; set; } = string.Empty;
        public int Order { get; set; }
        public long Size { get; set; }
        public string ContentType { get; set; } = "application/octet-stream";
    }
}
