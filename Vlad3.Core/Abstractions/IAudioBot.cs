using Vlad3.Core.Models;

namespace Vlad3.Core.Abstractions;

public interface IAudioBot : IAsyncDisposable
{
    string Id { get; }
    string Type { get; }
    string Label { get; }

    BotState State { get; }

    Task<IReadOnlyList<AudioChannelInfo>> GetAvailableChannelsAsync(CancellationToken cancellationToken = default);
    Task ConnectAsync(string channelId, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task PlayAsync(AudioTrackInfo track, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    event Func<AudioBotCommand, Task>? CommandReceived;

    Task RegisterCommandsAsync(CancellationToken cancellationToken = default);
}
