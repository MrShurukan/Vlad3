using Microsoft.Extensions.Logging;
using NetCord.Logging;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using NetCordLogLevel = NetCord.Logging.LogLevel;

namespace Vlad3.Bots.Discord;

internal sealed class NetCordLoggerAdapter : IGatewayLogger, IRestLogger, IVoiceLogger
{
    private readonly ILogger _logger;

    public NetCordLoggerAdapter(ILogger logger)
    {
        _logger = logger;
    }

    public void Log<TState>(NetCordLogLevel logLevel, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var mappedLevel = Map(logLevel);
        _logger.Log(mappedLevel, exception, formatter(state, exception));
    }

    public bool IsEnabled(NetCordLogLevel logLevel)
        => _logger.IsEnabled(Map(logLevel));

    private static MsLogLevel Map(NetCordLogLevel logLevel)
    {
        return logLevel switch
        {
            NetCordLogLevel.Trace => MsLogLevel.Trace,
            NetCordLogLevel.Debug => MsLogLevel.Debug,
            NetCordLogLevel.Information => MsLogLevel.Information,
            NetCordLogLevel.Warning => MsLogLevel.Warning,
            NetCordLogLevel.Error => MsLogLevel.Error,
            NetCordLogLevel.Critical => MsLogLevel.Critical,
            _ => MsLogLevel.None
        };
    }
}
