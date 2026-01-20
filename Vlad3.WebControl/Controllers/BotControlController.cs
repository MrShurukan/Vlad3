using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vlad3.Application.Bots;
using Vlad3.Core.Models;
using Vlad3.WebControl.Models;

namespace Vlad3.WebControl.Controllers;

[ApiController]
[Route("api/bots/{botId}")]
[Authorize(Policy = "OperatorOrAdmin")]
public sealed class BotControlController : ControllerBase
{
    private readonly IBotManager _botManager;

    public BotControlController(IBotManager botManager)
    {
        _botManager = botManager;
    }

    [HttpGet("status")]
    public async Task<ActionResult<BotStateDto>> GetStatus([FromRoute] string botId, CancellationToken cancellationToken)
    {
        var bot = await _botManager.GetBotAsync(botId, cancellationToken);
        return MapState(bot.State);
    }

    [HttpGet("channels")]
    public async Task<ActionResult<IReadOnlyList<AudioChannelInfo>>> GetChannels(
        [FromRoute] string botId,
        CancellationToken cancellationToken)
    {
        var channels = await _botManager.GetChannelsAsync(botId, cancellationToken);
        return channels.ToList();
    }

    [HttpPost("connect")]
    public async Task<IActionResult> Connect(
        [FromRoute] string botId,
        [FromBody] ConnectBotRequest request,
        CancellationToken cancellationToken)
    {
        await _botManager.ConnectAsync(botId, request.ChannelId, cancellationToken);
        return NoContent();
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect([FromRoute] string botId, CancellationToken cancellationToken)
    {
        await _botManager.DisconnectAsync(botId, cancellationToken);
        return NoContent();
    }

    [HttpPost("play")]
    public async Task<IActionResult> Play(
        [FromRoute] string botId,
        [FromBody] PlayBotRequest request,
        CancellationToken cancellationToken)
    {
        await _botManager.PlayAsync(botId, request.PlaylistId, request.TrackId, cancellationToken);
        return NoContent();
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop([FromRoute] string botId, CancellationToken cancellationToken)
    {
        await _botManager.StopAsync(botId, cancellationToken);
        return NoContent();
    }

    [HttpPost("next")]
    public async Task<IActionResult> Next([FromRoute] string botId, CancellationToken cancellationToken)
    {
        await _botManager.NextAsync(botId, cancellationToken);
        return NoContent();
    }

    [HttpPost("previous")]
    public async Task<IActionResult> Previous([FromRoute] string botId, CancellationToken cancellationToken)
    {
        await _botManager.PreviousAsync(botId, cancellationToken);
        return NoContent();
    }

    [HttpPost("autonext/toggle")]
    public async Task<ActionResult<AutoNextStateDto>> ToggleAutoNext(
        [FromRoute] string botId,
        CancellationToken cancellationToken)
    {
        var enabled = await _botManager.ToggleAutoNextAsync(botId, cancellationToken);
        return new AutoNextStateDto(enabled);
    }

    private static BotStateDto MapState(BotState state)
        => new(
            state.ConnectionState,
            state.PlaybackState,
            state.ConnectedChannelId,
            state.ConnectedChannelName,
            state.CurrentPlaylistId,
            state.CurrentTrackId,
            state.AutoNextEnabled);
}
