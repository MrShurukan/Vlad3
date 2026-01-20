using Microsoft.Extensions.Hosting;

namespace Vlad3.Application.Bots;

public sealed class BotManagerHostedService : IHostedService
{
    private readonly IBotManager _botManager;

    public BotManagerHostedService(IBotManager botManager)
    {
        _botManager = botManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => _botManager.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
