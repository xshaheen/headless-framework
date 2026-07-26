// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.CommitCoordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal sealed class OutboxMessageWriter(
    IDataStorage storage,
    IDispatcher dispatcher,
    TimeProvider timeProvider,
    IOptions<MessagingOptions> messagingOptions,
    ILogger<MessageOutboxBuffer> outboxBufferLogger,
    MessagingTelemetry? telemetry = null
)
{
    private readonly MessagingTelemetry _telemetry = telemetry ?? MessagingTelemetry.Default;

    internal async Task WriteAsync(
        PreparedPublishMessage publishRequest,
        DeliveryDecision decision,
        CancellationToken cancellationToken
    )
    {
        DeliveryMetadata.Stamp(publishRequest.Message.Headers, decision);
        MessagingTraceHandle traceHandle = default;
        try
        {
            traceHandle = _TracingBefore(publishRequest.Message, publishRequest.Lane);

            // Use the coordinator/transaction captured in the caller's frame — never re-read Current here. If the
            // captured transaction has since completed, StoreMessageAsync fails loudly rather than silently dropping
            // to the non-atomic immediate path.
            if (decision.Path is DeliveryPath.DurableCoordinated)
            {
                var coordinator =
                    decision.Coordination.Coordinator
                    ?? throw new InvalidOperationException("Coordinated delivery is missing its commit coordinator.");
                var transaction =
                    decision.Coordination.Transaction
                    ?? throw new InvalidOperationException(
                        "Coordinated delivery is missing its relational transaction."
                    );
                var mediumMessage = await storage
                    .StoreMessageAsync(
                        publishRequest.MessageName,
                        _CreateStorageEnvelope(publishRequest),
                        transaction,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                _TracingAfter(traceHandle, publishRequest.Message, publishRequest.Lane);

                var bufferState = new MessageOutboxBufferState(
                    dispatcher,
                    messagingOptions.Value.OutboxFlushTimeout,
                    timeProvider,
                    outboxBufferLogger
                );
                var buffer = coordinator.GetOrAdd(
                    bufferState,
                    static (coordinator, state) =>
                        new MessageOutboxBuffer(
                            coordinator,
                            state.Dispatcher,
                            state.FlushTimeout,
                            state.TimeProvider,
                            state.Logger
                        )
                );
                buffer.Add(mediumMessage);

                return;
            }

            // No ambient coordinator (or no relational transaction on it): store immediately with no transaction
            // and dispatch in-band. The message is persisted and enqueued in one shot — no atomic enlistment.
            var immediateMessage = await storage
                .StoreMessageAsync(
                    publishRequest.MessageName,
                    _CreateStorageEnvelope(publishRequest),
                    transaction: null,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            _TracingAfter(traceHandle, publishRequest.Message, publishRequest.Lane);

            if (decision.PublishAt is { } publishAt)
            {
                await dispatcher
                    .EnqueueToScheduler(
                        immediateMessage,
                        publishAt,
                        transaction: null,
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                await dispatcher.EnqueueToPublish(immediateMessage, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Benign cancellation (caller/shutdown) is not a persist failure: stop (export) the span without
            // an error status, matching the publish/subscriber-invoke emission sites. Rethrow unchanged.
            traceHandle.Activity?.Dispose();
            throw;
        }
        catch (Exception e)
        {
            _TracingError(traceHandle, e);

            throw;
        }
    }

    private readonly record struct MessageOutboxBufferState(
        IDispatcher Dispatcher,
        TimeSpan FlushTimeout,
        TimeProvider TimeProvider,
        ILogger<MessageOutboxBuffer> Logger
    );

    private static MediumMessage _CreateStorageEnvelope(PreparedPublishMessage publishRequest)
    {
        return new()
        {
            StorageId = Guid.Empty,
            Origin = publishRequest.Message,
            Content = string.Empty,
            Lane = publishRequest.Lane,
        };
    }

    #region Tracing

    private MessagingTraceHandle _TracingBefore(Message message, MessageLane lane)
    {
        if (!MessagingDiagnostics.IsEnabled)
        {
            return default;
        }

        var now = _NowUnixTimeMilliseconds();
        var activity = _telemetry.PersistStart(message, message.Name, lane, now);

        return new MessagingTraceHandle(activity, now);
    }

    private void _TracingAfter(MessagingTraceHandle traceHandle, Message message, MessageLane lane)
    {
        if (!traceHandle.IsRecording)
        {
            return;
        }

        var now = _NowUnixTimeMilliseconds();
        MessagingTelemetry.PersistStop(
            traceHandle.Activity,
            message.Name,
            traceHandle.StartTimestampMs!.Value,
            now,
            lane,
            DeliveryMetadata.Read(message.Headers)
        );
    }

    private static void _TracingError(MessagingTraceHandle traceHandle, Exception ex)
    {
        if (!traceHandle.IsRecording)
        {
            return;
        }

        MessagingTelemetry.PersistError(traceHandle.Activity, ex);
    }

    private long _NowUnixTimeMilliseconds()
    {
        return timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
    }

    #endregion
}
