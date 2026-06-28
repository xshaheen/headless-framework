// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.CommitCoordination;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Transactions;

internal sealed partial class MessageOutboxBuffer : InMemoryWorkBuffer<MediumMessage>
{
    private readonly IDispatcher _dispatcher;
    private readonly TimeSpan _flushTimeout;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MessageOutboxBuffer> _logger;

    public MessageOutboxBuffer(
        ICommitCoordinator coordinator,
        IDispatcher dispatcher,
        TimeSpan flushTimeout,
        TimeProvider timeProvider,
        ILogger<MessageOutboxBuffer> logger
    )
    {
        _dispatcher = dispatcher;
        _flushTimeout = flushTimeout;
        _timeProvider = timeProvider;
        _logger = logger;
        coordinator.OnCommit(_FlushAsync);
    }

    private async ValueTask _FlushAsync(CommitContext context, CancellationToken cancellationToken)
    {
        // The drain deliberately runs with CancellationToken.None (D9 — a committed dispatch must not be
        // abandoned because the request was cancelled), so an unresponsive broker would otherwise hold the
        // drain — and the request thread, DI scope, and DB connection — indefinitely. Bound it with an
        // independent timeout: messages are already durably stored in-transaction, so any not dispatched
        // before the deadline are recovered by the relay sweep (dispatch is acceleration, not correctness).
        using var timeoutCts = new CancellationTokenSource(_flushTimeout, _timeProvider);

        List<Exception>? faults = null;

        try
        {
            foreach (var message in Drain())
            {
                try
                {
                    if (message.Origin.Headers.ContainsKey(Headers.DelayTime))
                    {
                        await _dispatcher
                            .EnqueueToScheduler(
                                message,
                                DateTime.Parse(message.Origin.Headers[Headers.SentTime]!, CultureInfo.InvariantCulture),
                                transaction: null,
                                timeoutCts.Token
                            )
                            .ConfigureAwait(false);

                        continue;
                    }

                    await _dispatcher.EnqueueToPublish(message, timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    // Timeout is terminal for the whole flush — stop and report via the outer handler.
                    throw;
                }
                catch (Exception ex)
                {
                    // One message's broker fault must not abandon the rest of the buffer: record which message
                    // failed, keep dispatching the remainder, and surface the aggregate so the drain still faults.
                    // Failed messages remain durable and are recovered by the relay sweep.
                    (faults ??= []).Add(ex);
                    LogMessageDispatchFailed(_logger, message.StorageId, ex);
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            // Latency degradation, not data loss: the undispatched messages remain in the durable store and the
            // relay sweep picks them up. Surface it so a chronically slow broker is diagnosable. A timeout subsumes
            // any per-message broker faults recorded earlier in this same flush — those were already logged
            // individually (LogMessageDispatchFailed) and are equally relay-recoverable, so the timeout is the
            // single drain signal here rather than re-throwing the accumulated faults.
            LogFlushTimedOut(_logger, _flushTimeout);

            return;
        }

        if (faults is { Count: > 0 })
        {
            if (faults.Count == 1)
            {
                ExceptionDispatchInfo.Capture(faults[0]).Throw();
            }

            throw new AggregateException(faults);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Outbox flush exceeded the {FlushTimeout} timeout; undispatched messages remain durable and will be recovered by the relay sweep."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogFlushTimedOut(ILogger logger, TimeSpan flushTimeout);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Outbox flush failed to dispatch message {StorageId}; it remains durable and will be recovered by the relay sweep."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogMessageDispatchFailed(ILogger logger, Guid storageId, Exception exception);
}
