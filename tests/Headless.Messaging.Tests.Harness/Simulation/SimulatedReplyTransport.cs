// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;

namespace Tests.Simulation;

/// <summary>
/// The simulated broker's reply channel. Listeners register at the broker rather than on a transport instance, so every
/// host on one broker can reply to every other one, as processes on one real broker can.
/// </summary>
internal sealed class SimulatedReplyTransport(SimulatedBroker broker, ILogger<SimulatedReplyTransport> logger)
    : IReplyTransport
{
    public ValueTask<IReplyListener> OpenListenerAsync(
        Func<TransportMessage, CancellationToken, ValueTask> onReply,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(onReply);
        cancellationToken.ThrowIfCancellationRequested();

#pragma warning disable CA2000 // False positive: the returned listener is owned and disposed by the caller.
        return ValueTask.FromResult<IReplyListener>(new SimulatedReplyListener(broker, onReply, logger));
#pragma warning restore CA2000
    }

    public ValueTask SendAsync(string address, TransportMessage reply, CancellationToken cancellationToken = default)
    {
        Argument.IsTrue(
            ((IReplyTransport)this).IsReplyAddress(address),
            "A reply can only be sent to an address inside the reserved reply namespace.",
            nameof(address)
        );
        Argument.IsNotNull(reply);
        cancellationToken.ThrowIfCancellationRequested();

        broker.SendReply(address, reply);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// One process's reply channel at the simulated broker. Replies are queued and handed to the handler on a pump of their
/// own, so a responder's send never runs the caller's reply handling on its own stack.
/// </summary>
internal sealed class SimulatedReplyListener : IReplyListener
{
    private readonly SimulatedBroker _broker;
    private readonly Func<TransportMessage, CancellationToken, ValueTask> _onReply;
    private readonly ILogger _logger;

    private readonly Channel<TransportMessage> _replies = Channel.CreateUnbounded<TransportMessage>(
        new UnboundedChannelOptions { SingleReader = true }
    );

    private readonly CancellationTokenSource _closing = new();
    private readonly Task _pump;
    private int _disposed;

    public SimulatedReplyListener(
        SimulatedBroker broker,
        Func<TransportMessage, CancellationToken, ValueTask> onReply,
        ILogger logger
    )
    {
        _broker = broker;
        _onReply = onReply;
        _logger = logger;
        Address = ReplyAddresses.Create();
        _pump = Task.Run(_PumpAsync);
        broker.RegisterReplyListener(this);
    }

    public string Address { get; }

    public ValueTask<string> WaitForAddressAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Ensure.NotDisposed(Volatile.Read(ref _disposed) != 0, this);

        // The listener is registered at the broker before it is handed out, so its address is live at once.
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

        _broker.UnregisterReplyListener(this);
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
                var keepReceiving = await ReplyHandlerInvoker
                    .InvokeAsync(
                        _onReply,
                        reply,
                        (Logger: _logger, Address),
                        static (state, e) => state.Logger.ReplyHandlerFailed(e, state.Address),
                        _closing.Token
                    )
                    .ConfigureAwait(false);

                if (!keepReceiving)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Closing drops the replies still queued: their callers are being failed by the requester's shutdown.
        }
    }
}

internal static partial class SimulatedReplyListenerLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "The reply handler of simulated reply listener '{ReplyAddress}' threw; the listener keeps receiving."
    )]
    public static partial void ReplyHandlerFailed(this ILogger logger, Exception exception, string replyAddress);
}
