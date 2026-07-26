// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.CommitCoordination;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Internal;

internal sealed class OutboxMessageWriter(
    IDataStorage storage,
    IDispatcher dispatcher,
    TimeProvider timeProvider,
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
                var mediumMessage = await _StoreMessageAsync(publishRequest, decision, transaction, cancellationToken)
                    .ConfigureAwait(false);

                _TracingAfter(traceHandle, publishRequest.Message, publishRequest.Lane);

                var bufferState = new MessageOutboxBufferState(dispatcher);
                var buffer = coordinator.GetOrAdd(
                    bufferState,
                    static (coordinator, state) => new MessageOutboxBuffer(coordinator, state.Dispatcher)
                );
                buffer.Add(mediumMessage);

                return;
            }

            // No ambient coordinator (or no relational transaction on it): commit the durable row first.
            // Dispatch after this boundary is non-blocking acceleration; retry/delayed pickup owns recovery.
            var immediateMessage = await _StoreMessageAsync(
                    publishRequest,
                    decision,
                    transaction: null,
                    cancellationToken
                )
                .ConfigureAwait(false);

            _TracingAfter(traceHandle, publishRequest.Message, publishRequest.Lane);

            if (decision.PublishAt is not null)
            {
                (dispatcher as ICommittedDelayedMessageDispatcher)?.EnqueueCommittedDelayedMessage(immediateMessage);
            }
            else
            {
                (dispatcher as ICommittedMessageDispatcher)?.EnqueueCommittedMessage(immediateMessage);
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

    private readonly record struct MessageOutboxBufferState(IDispatcher Dispatcher);

    private ValueTask<MediumMessage> _StoreMessageAsync(
        PreparedPublishMessage publishRequest,
        DeliveryDecision decision,
        System.Data.Common.DbTransaction? transaction,
        CancellationToken cancellationToken
    )
    {
        var envelope = _CreateStorageEnvelope(publishRequest);
        return decision.PublishAt is { } publishAt
            ? storage.StoreScheduledMessageAsync(
                publishRequest.MessageName,
                envelope,
                publishAt,
                transaction,
                cancellationToken
            )
            : storage.StoreMessageAsync(publishRequest.MessageName, envelope, transaction, cancellationToken);
    }

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
