// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

internal sealed class OutboxMessageWriter(
    IDataStorage storage,
    IDispatcher dispatcher,
    TimeProvider timeProvider,
    MessagingTelemetry? telemetry = null
)
{
    private readonly MessagingTelemetry _telemetry = telemetry ?? MessagingTelemetry.Default;

    internal async Task<Guid> WriteAsync(
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

            // Use the unit of work/transaction captured in the caller's frame — never re-read Current here. If the
            // captured transaction has since completed, StoreMessageAsync fails loudly rather than silently dropping
            // to the non-atomic immediate path.
            if (decision.Path is DeliveryPath.DurableCoordinated)
            {
                var unitOfWork =
                    decision.Coordination.UnitOfWork
                    ?? throw new InvalidOperationException("Coordinated delivery is missing its unit of work.");

                // Obtained before the store on purpose: this is the first registration on the caller's handle, so
                // a handle that can no longer carry work — a completed nested view, a terminal unit — is refused
                // here, before any storage effect. The buffer registers its drain only when the first row is
                // added below, after the store, because a non-relational store enlists its own promotion callback
                // inside StoreCoordinatedMessageAsync and callbacks drain in registration order.
                var buffer = unitOfWork.GetOrAdd(
                    dispatcher,
                    static (_, committedDispatcher) => new MessageOutboxBuffer(committedDispatcher)
                );

                var mediumMessage = await _StoreCoordinatedMessageAsync(
                        publishRequest,
                        decision,
                        unitOfWork,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                _TracingAfter(traceHandle, publishRequest.Message, publishRequest.Lane);

                buffer.Add(unitOfWork, mediumMessage);

                return mediumMessage.StorageId;
            }

            // No active unit of work (or no relational transaction on it): commit the durable row first.
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

            return immediateMessage.StorageId;
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

    private ValueTask<MediumMessage> _StoreCoordinatedMessageAsync(
        PreparedPublishMessage publishRequest,
        DeliveryDecision decision,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken
    )
    {
        if (decision.Coordination.Transaction is { } transaction)
        {
            return _StoreMessageAsync(publishRequest, decision, transaction, cancellationToken);
        }

        // A compatible unit without a relational handle is only valid for a storage that captures rows on the
        // unit itself; anything else must fail here rather than fall through to a standalone durable write
        // that would survive the caller's rollback.
        if (storage is not ICoordinatedMessageStore coordinatedStore)
        {
            throw new InvalidOperationException(
                $"Coordinated delivery is missing its relational transaction and '{storage.GetType().Name}' cannot capture rows on the unit of work."
            );
        }

        return coordinatedStore.StoreCoordinatedMessageAsync(
            publishRequest.MessageName,
            _CreateStorageEnvelope(publishRequest),
            decision.PublishAt,
            unitOfWork,
            cancellationToken
        );
    }

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
