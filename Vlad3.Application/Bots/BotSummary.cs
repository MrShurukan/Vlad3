using Vlad3.Core.Models;

namespace Vlad3.Application.Bots;

public sealed record BotSummary(BotConfiguration Configuration, BotState State);
