using System.Threading.Channels;

namespace Featly.Sdk.Internal;

/// <summary>
/// Bounded-channel sink. <see cref="Enqueue"/> is a lock-free <c>TryWrite</c>
/// that drops the event when the buffer is full, so the evaluation hot path is
/// never blocked by a slow or unreachable server. <see cref="FeatlyEventFlushService"/>
/// drains the reader and uploads in batches.
/// </summary>
internal sealed class ChannelEventSink : IEventSink
{
    private readonly Channel<QueuedEvent> _channel;

    public ChannelEventSink(int capacity = 10_000)
    {
        _channel = Channel.CreateBounded<QueuedEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
    }

    public ChannelReader<QueuedEvent> Reader => _channel.Reader;

    public void Enqueue(QueuedEvent evt) => _channel.Writer.TryWrite(evt);
}
