// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Runtime;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.RequestReply;

/// <summary>
/// Owns this process's reply listener across the host lifecycle. The bootstrapper starts it before any consumer
/// processor, and on shutdown it fails every waiting call before it closes the listener.
/// </summary>
internal sealed class ReplyListenerHost(
    IReplyTransport transport,
    ReplyDispatcher dispatcher,
    PendingRequests pending,
    TimeProvider timeProvider,
    ILogger<ReplyListenerHost> logger
) : IProcessingServer, IProcessingServerShutdown
{
    private readonly TaskCompletionSource<IReplyListener> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private volatile bool _stopping;
    private int _closed;

    public async ValueTask StartAsync(CancellationToken stoppingToken)
    {
        if (_stopping)
        {
            return;
        }

        IReplyListener listener;
        try
        {
            listener = await transport.OpenListenerAsync(dispatcher.DispatchAsync, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Bootstrap fails with this exception; a waiting call fails with RequestNotSentException.
            if (_ready.TrySetException(e))
            {
                _ = _ready.Task.Exception;
            }

            throw;
        }

        // Shutdown can begin while the listener opens; the late listener is closed rather than leaked.
        if (_stopping || !_ready.TrySetResult(listener))
        {
            await listener.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits until the listener is ready and returns the reply address a request must carry.
    /// </summary>
    /// <exception cref="RequestNotSentException">The requester is stopping, or its listener failed to open.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async ValueTask<string> WaitForAddressAsync(CancellationToken cancellationToken)
    {
        if (_stopping)
        {
            throw Stopping();
        }

        IReplyListener listener;
        try
        {
            listener = await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            throw _stopping
                ? Stopping()
                : new RequestNotSentException(
                    "The reply listener failed to open, so no request can be sent.",
                    requestId: null,
                    e
                );
        }

        try
        {
            return await listener.WaitForAddressAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException e)
        {
            throw Stopping(innerException: e);
        }
    }

    public void Quiesce()
    {
        _stopping = true;
        pending.Close();
        if (_ready.TrySetException(new ObjectDisposedException(nameof(ReplyListenerHost))))
        {
            // Waiters map this to RequestNotSentException; with none, the fault must not surface as unobserved.
            _ = _ready.Task.Exception;
        }
    }

    public async ValueTask StopAsync(TimeSpan timeout)
    {
        Quiesce();

        if (Interlocked.Exchange(ref _closed, 1) != 0 || !_ready.Task.IsCompletedSuccessfully)
        {
            return;
        }

        try
        {
            // Bounded by the bootstrapper's shutdown budget; the pending calls already failed above, so nothing waits.
            var listener = await _ready.Task.ConfigureAwait(false);
            await listener.DisposeAsync().AsTask().WaitAsync(timeout, timeProvider).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.ReplyListenerCloseFailed(e);
        }
    }

    public ValueTask DisposeAsync()
    {
        return StopAsync(Timeout.InfiniteTimeSpan);
    }

    /// <summary>Creates the failure of a call refused because the requester is stopping.</summary>
    internal static RequestNotSentException Stopping(string? requestId = null, Exception? innerException = null)
    {
        return new RequestNotSentException(
            "The requester is stopping and sends no new requests.",
            requestId,
            innerException
        )
        {
            IsRequesterStopping = true,
        };
    }
}
