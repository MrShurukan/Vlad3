using Vlad3.Core.Models;

namespace Vlad3.Application.Playlists;

public interface IPlaylistService
{
    Task<IReadOnlyList<PlaylistInfo>> GetPlaylistsAsync(CancellationToken cancellationToken = default);
    Task<PlaylistInfo> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default);
    Task DeletePlaylistAsync(string playlistId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlaylistTrack>> GetTracksAsync(string playlistId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlaylistTrack>> AddTracksAsync(
        string playlistId,
        IReadOnlyList<UploadFile> files,
        TrackInsertPosition? insertPosition,
        CancellationToken cancellationToken = default);
    Task DeleteTrackAsync(string playlistId, string trackId, CancellationToken cancellationToken = default);
    Task ReorderTracksAsync(
        string playlistId,
        IReadOnlyList<string> orderedTrackIds,
        CancellationToken cancellationToken = default);
    Task<AudioTrackInfo> GetTrackFileAsync(string playlistId, string trackId, CancellationToken cancellationToken = default);
}
