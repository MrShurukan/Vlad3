namespace Vlad3.Core.Models;

public enum BotConnectionState
{
    Disconnected = 0,
    Connected = 1
}

public enum BotPlaybackState
{
    Stopped = 0,
    Playing = 1
}

public enum BotCommandType
{
    Play = 0,
    Stop = 1,
    Next = 2,
    Previous = 3,
    Connect = 4,
    Disconnect = 5
}
