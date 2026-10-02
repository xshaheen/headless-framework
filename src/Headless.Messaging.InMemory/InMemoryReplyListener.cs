// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.InMemory;

/// <summary>
/// An in-process reply channel. Replies are queued and handed to the handler on a pump of their own, so a responder's
/// send never runs the caller's reply handling on its own stack, as with a real broker.
/// </summary>
internal sealed class InMemoryReplyListener : IReplyListener
{
    private readonly MemoryQueue _queue;
    private readonly Func<TransportMessage, CancellationToken, ValueTask> _onReply;
    private readonly ILogger _logger;

    private readonly Channel<TransportMessage> _replies = Channel.CreateUnbounded<TransportMessage>(
        new UnboundedChannelOptions { SingleReader = true }
    );

    private readonly CancellationTokenSource _closing = new();
    private readonly Task _pump;
    private int _disposed;

    public InMemoryReplyListener(
        MemoryQueue queue,
        Func<TransportMessage, CancellationToken, ValueTask> onReply,
        ILogger logger
    )
    {
        _queue = queue;
        _onReply = onReply;
        _logger = logger;
        Address = ReplyAddresses.Create();
        _pump = Task.Run(_PumpAsync);
        queue.RegisterReplyListener(Address, this);
    }

    public string Address { get; }

    public ValueTask<string> WaitForAddressAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Ensure.NotDisposed(Volatile.Read(ref _disposed) != 0, this);

        // An in-process channel is ready as soon as it is registered and never loses its connection.
        return ValueTask.FromResult(Address);
    }

    /// <summary>Queues a reply for the handler; a reply arriving after the listener closed is dropped.</summary>
    internal void Deliver(TransportMessage reply)
    {
        _replies.Writer.TryWrite(reply);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.UnregisterReplyListener(Address, this);
        _replies.Writer.TryComplete();
        await _closing.CancelAsync().ConfigureAwait(false);
        await _pump.ConfigureAwait(false);
        _closing.Dispose();
    }

    private async Task _PumpAsync()
    {
        try
        {
            await foreach (var reply in _replies.Reader.ReadAllAsync(_closing.Token).ConfigureAwait(false))
            {
                try
                {
                    await _onReply(reply, _closing.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_closing.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    // One faulty reply must not close the channel every other pending call depends on.
                    _logger.ReplyHandlerFailed(e, Address);
                }
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Closing drops the replies still queued: their callers are being failed by the requester's shutdown.
        }
    }
}

internal static partial class InMemoryReplyListenerLog
{
    [LoggerMessage(
        EventId = 3012,
        Level = LogLevel.Error,
        Message = "The reply handler of in-memory reply listener '{ReplyAddress}' threw; the listener keeps receiving."
    )]
    public static partial void ReplyHandlerFailed(this ILogger logger, Exception exception, string replyAddress);
}
