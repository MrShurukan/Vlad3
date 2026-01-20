using Microsoft.Extensions.Logging;
using Vlad3.Core.Abstractions;
using Vlad3.Core.Models;

namespace Vlad3.Bots.TeamSpeak;

public sealed class TeamSpeakAudioBotFactory : IAudioBotFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public TeamSpeakAudioBotFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string Type => "teamspeak";
    public string DisplayName => "TeamSpeak";

    public Task<IAudioBot> CreateAsync(BotConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<TeamSpeakAudioBot>();
        IAudioBot bot = new TeamSpeakAudioBot(configuration, logger);
        return Task.FromResult(bot);
    }
}
