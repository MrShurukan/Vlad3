using Microsoft.Extensions.Logging;
using Vlad3.Core.Abstractions;
using Vlad3.Core.Models;

namespace Vlad3.Bots.Discord;

public sealed class DiscordAudioBotFactory : IAudioBotFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public DiscordAudioBotFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string Type => "discord";
    public string DisplayName => "Discord";

    public Task<IAudioBot> CreateAsync(BotConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<DiscordAudioBot>();
        IAudioBot bot = new DiscordAudioBot(configuration, logger);
        return Task.FromResult(bot);
    }
}
