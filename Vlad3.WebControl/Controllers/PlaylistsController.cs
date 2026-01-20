using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vlad3.Application.Playlists;
using Vlad3.Core.Models;
using Vlad3.WebControl.Models;

namespace Vlad3.WebControl.Controllers;

[ApiController]
[Route("api/playlists")]
public sealed class PlaylistsController : ControllerBase
{
    private readonly IPlaylistService _playlistService;

    public PlaylistsController(IPlaylistService playlistService)
    {
        _playlistService = playlistService;
    }

    [HttpGet]
    [Authorize(Policy = "OperatorOrAdmin")]
    public async Task<ActionResult<IReadOnlyList<PlaylistDto>>> GetPlaylists(CancellationToken cancellationToken)
    {
        var playlists = await _playlistService.GetPlaylistsAsync(cancellationToken);
        return playlists.Select(item => new PlaylistDto(item.Id, item.Name, item.TrackCount)).ToList();
    }

    [HttpPost]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<PlaylistDto>> CreatePlaylist(
        [FromBody] CreatePlaylistRequest request,
        CancellationToken cancellationToken)
    {
        var playlist = await _playlistService.CreatePlaylistAsync(request.Name, cancellationToken);
        return new PlaylistDto(playlist.Id, playlist.Name, playlist.TrackCount);
    }

    [HttpDelete("{playlistId}")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> DeletePlaylist([FromRoute] string playlistId, CancellationToken cancellationToken)
    {
        await _playlistService.DeletePlaylistAsync(playlistId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{playlistId}/tracks")]
    [Authorize(Policy = "OperatorOrAdmin")]
    public async Task<ActionResult<IReadOnlyList<TrackDto>>> GetTracks(
        [FromRoute] string playlistId,
        CancellationToken cancellationToken)
    {
        var tracks = await _playlistService.GetTracksAsync(playlistId, cancellationToken);
        return tracks.Select(MapTrack).ToList();
    }

    [HttpPost("{playlistId}/tracks")]
    [Authorize(Roles = UserRoles.Admin)]
    [RequestSizeLimit(524_288_000)]
    public async Task<ActionResult<IReadOnlyList<TrackDto>>> UploadTracks(
        [FromRoute] string playlistId,
        [FromForm] List<IFormFile> files,
        [FromQuery] string? insertAfterTrackId,
        [FromQuery] int? insertAfterOrder,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return BadRequest(new { error = "No files uploaded." });
        }

        var uploads = files.Select(file => new UploadFile(
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            file.OpenReadStream(),
            file.Length)).ToList();

        try
        {
            var position = new TrackInsertPosition(insertAfterTrackId, insertAfterOrder);
            var tracks = await _playlistService.AddTracksAsync(playlistId, uploads, position, cancellationToken);
            return tracks.Select(MapTrack).ToList();
        }
        finally
        {
            foreach (var upload in uploads)
            {
                upload.Content.Dispose();
            }
        }
    }

    [HttpDelete("{playlistId}/tracks/{trackId}")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> DeleteTrack(
        [FromRoute] string playlistId,
        [FromRoute] string trackId,
        CancellationToken cancellationToken)
    {
        await _playlistService.DeleteTrackAsync(playlistId, trackId, cancellationToken);
        return NoContent();
    }

    [HttpPut("{playlistId}/tracks/reorder")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> ReorderTracks(
        [FromRoute] string playlistId,
        [FromBody] ReorderTracksRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TrackIds.Count == 0)
        {
            return BadRequest(new { error = "TrackIds are required." });
        }

        await _playlistService.ReorderTracksAsync(playlistId, request.TrackIds, cancellationToken);
        return NoContent();
    }

    private static TrackDto MapTrack(PlaylistTrack track)
        => new(track.Id, track.OriginalName, track.Order, track.Size, track.ContentType);
}
