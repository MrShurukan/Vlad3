using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vlad3.Application.Bots;
using Vlad3.Core.Models;
using Vlad3.WebControl.Models;

namespace Vlad3.WebControl.Controllers;

[ApiController]
[Route("api/bots")]
public sealed class BotsController : ControllerBase
{
    private readonly IBotManager _botManager;

    public BotsController(IBotManager botManager)
    {
        _botManager = botManager;
    }

    [HttpGet]
    [Authorize(Policy = "OperatorOrAdmin")]
    public async Task<ActionResult<IReadOnlyList<BotDto>>> GetBots(CancellationToken cancellationToken)
    {
        var bots = await _botManager.GetBotsAsync(cancellationToken);
        return bots.Select(MapBot).ToList();
    }

    [HttpGet("types")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<IReadOnlyList<BotTypeDto>>> GetTypes(CancellationToken cancellationToken)
    {
        var types = await _botManager.GetBotTypesAsync(cancellationToken);
        return types.Select(type => new BotTypeDto(type.Type, type.DisplayName)).ToList();
    }

    [HttpPost]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<BotDto>> CreateBot([FromBody] CreateBotRequest request, CancellationToken cancellationToken)
    {
        var bot = await _botManager.CreateBotAsync(
            request.Type,
            request.Label,
            request.ApiKey,
            request.Settings,
            cancellationToken);

        return MapBot(bot);
    }

    [HttpPut("{botId}")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<BotDto>> UpdateBot(
        [FromRoute] string botId,
        [FromBody] UpdateBotRequest request,
        CancellationToken cancellationToken)
    {
        var bot = await _botManager.UpdateBotAsync(
            botId,
            request.Label,
            request.ApiKey,
            request.Settings,
            cancellationToken);

        return MapBot(bot);
    }

    [HttpDelete("{botId}")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> DeleteBot([FromRoute] string botId, CancellationToken cancellationToken)
    {
        await _botManager.DeleteBotAsync(botId, cancellationToken);
        return NoContent();
    }

    private static BotDto MapBot(BotSummary summary)
    {
        var state = summary.State;
        return new BotDto(
            summary.Configuration.Id,
            summary.Configuration.Type,
            summary.Configuration.Label,
            summary.Configuration.Settings,
            new BotStateDto(
                state.ConnectionState,
                state.PlaybackState,
                state.ConnectedChannelId,
                state.ConnectedChannelName,
                state.CurrentPlaylistId,
                state.CurrentTrackId,
                state.AutoNextEnabled));
    }
}
