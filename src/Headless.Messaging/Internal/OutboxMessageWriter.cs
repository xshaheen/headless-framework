// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

internal sealed class OutboxMessageWriter(
    IDataStorage storage,
    IDispatcher dispatcher,
    IMessageSerializer serializer,
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

            // Serialized once: the stored row embeds the transport body, and the send that follows reuses it.
            var transport = await serializer
                .SerializeToTransportMessageAsync(publishRequest.Message, cancellationToken)
                .ConfigureAwait(false);
            var envelope = new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = publishRequest.Message,
                Content = serializer.Serialize(publishRequest.Message, transport.Body),
                Lane = publishRequest.Lane,
            };

            // Use the unit of work/transaction captured in the caller's frame — never re-read Current here. If the
            // captured transaction has since completed, StoreMessageAsync fails loudly rather than silently dropping
            // to the non-atomic immediate path.
            if (decision.Path is DeliveryPath.DurableEnlisted)
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
                        publishRequest.MessageName,
                        envelope,
                        decision,
                        unitOfWork,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                _TracingAfter(traceHandle, publishRequest.Message, publishRequest.Lane);

                _AttachPreparedTransport(mediumMessage, envelope.Origin, transport);
                buffer.Add(unitOfWork, mediumMessage);

                return mediumMessage.StorageId;
            }

            // No active unit of work (or no relational transaction on it): commit the durable row first.
            // Dispatch after this boundary is non-blocking acceleration; retry/delayed pickup owns recovery.
            var immediateMessage = await _StoreMessageAsync(
                    storage,
                    publishRequest.MessageName,
                    envelope,
                    decision,
                    transaction: null,
                    cancellationToken
                )
                .ConfigureAwait(false);

            _TracingAfter(traceHandle, publishRequest.Message, publishRequest.Lane);

            _AttachPreparedTransport(immediateMessage, envelope.Origin, transport);

            if (decision.PublishAt is not null)
            {
                // The row is unclaimed Delayed work, so a refusal needs no hand-back: the delayed processor claims it.
                _ = (dispatcher as ICommittedDelayedMessageDispatcher)?.EnqueueCommittedDelayedMessage(
                    immediateMessage
                );
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

    // A storage keeps the origin it was given, so the transport serialized for that origin can ride along to the send.
    private static void _AttachPreparedTransport(MediumMessage stored, Message origin, TransportMessage transport)
    {
        if (ReferenceEquals(stored.Origin, origin))
        {
            stored.PreparedTransport = (origin, transport);
        }
    }

    private ValueTask<MediumMessage> _StoreCoordinatedMessageAsync(
        string messageName,
        MediumMessage envelope,
        DeliveryDecision decision,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken
    )
    {
        if (decision.Coordination.Transaction is { } transaction)
        {
            return decision.Coordination.Outbox is { } outbox
                ? _StoreInOutboxAsync(outbox, messageName, envelope, decision, transaction, cancellationToken)
                : _StoreMessageAsync(storage, messageName, envelope, decision, transaction, cancellationToken);
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
            messageName,
            envelope,
            decision.PublishAt,
            unitOfWork,
            cancellationToken
        );
    }

    // The unit's transaction belongs to an additional outbox's database: the row is written there, and the stamp
    // routes the relay's later state changes of this row back to the same database.
    // An outbox whose database was unreachable at startup may not be initialized yet. The open transaction proves
    // the database is reachable now, so initialize it here rather than fail the write on a missing table.
    private static async ValueTask<MediumMessage> _StoreInOutboxAsync(
        MessagingOutbox outbox,
        string messageName,
        MediumMessage envelope,
        DeliveryDecision decision,
        System.Data.Common.DbTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await outbox.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var message = await _StoreMessageAsync(
                outbox.Storage,
                messageName,
                envelope,
                decision,
                transaction,
                cancellationToken
            )
            .ConfigureAwait(false);
        message.OutboxStorage = outbox.Storage;

        return message;
    }

    private static ValueTask<MediumMessage> _StoreMessageAsync(
        IDataStorage target,
        string messageName,
        MediumMessage envelope,
        DeliveryDecision decision,
        System.Data.Common.DbTransaction? transaction,
        CancellationToken cancellationToken
    )
    {
        return decision.PublishAt is { } publishAt
            ? target.StoreScheduledMessageAsync(messageName, envelope, publishAt, transaction, cancellationToken)
            : target.StoreMessageAsync(messageName, envelope, transaction, cancellationToken);
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
