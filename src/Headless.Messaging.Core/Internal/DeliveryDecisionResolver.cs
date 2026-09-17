// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

internal enum DeliveryPath
{
    Direct = 0,
    DurableStandalone = 1,
    DurableCoordinated = 2,
}

internal readonly record struct DeliveryDecision(
    DeliveryMode RequestedMode,
    DeliveryMode ResolvedMode,
    TransactionEnlistment Enlistment,
    DeliveryPath Path,
    TimeSpan? Delay,
    DateTimeOffset? PublishAt,
    DeliveryCoordination Coordination,
    // The caller's absolute instant, retained separately from PublishAt so the publish context can fence
    // middleware against changing it. PublishAt is the resolved not-before regardless of which form produced it.
    DateTimeOffset? ScheduledAt = null
)
{
    internal bool IsTransactional => Path is DeliveryPath.DurableCoordinated;
}

/// <summary>
/// Decides the delivery path before any storage or transport effect. The matrix (KD6/KD7):
/// <list type="bullet">
/// <item><c>Durable</c> (default): enlists in a compatible active unit of work when
/// <see cref="TransactionEnlistment"/> allows it; otherwise a standalone durable row. A unit with no joinable
/// resource behaves like no unit at all.</item>
/// <item><c>Direct</c>: transport now, bypassing storage and coordination checks entirely; rejects any
/// schedule and <see cref="TransactionEnlistment.Required"/>.</item>
/// </list>
/// <see cref="TransactionEnlistment.Required"/> with no compatible unit of work throws before any effect;
/// <see cref="TransactionEnlistment.Never"/> always writes standalone, even against an incompatible resource;
/// an incompatible resource otherwise throws regardless of <see cref="TransactionEnlistment.WhenAvailable"/> or
/// <see cref="TransactionEnlistment.Required"/>.
/// </summary>
internal static class DeliveryDecisionResolver
{
    internal static DeliveryDecision Resolve(
        MessageLane lane,
        DeliveryMode requestedMode,
        TransactionEnlistment enlistment,
        TimeSpan? delay,
        DeliveryCoordination coordination,
        DateTimeOffset now,
        DateTimeOffset? scheduledAt = null,
        bool storageSupported = true
    ) =>
        Resolve(
            lane,
            requestedMode,
            enlistment,
            delay,
            coordination.Status,
            now,
            coordination,
            scheduledAt,
            storageSupported
        );

    // Manually constructed middleware contexts need delivery semantics without live transaction resources.
    internal static DeliveryDecision Resolve(
        MessageLane lane,
        DeliveryMode requestedMode,
        TransactionEnlistment enlistment,
        TimeSpan? delay,
        DeliveryCoordinationStatus coordinationStatus,
        DateTimeOffset now,
        DeliveryCoordination coordination = default,
        DateTimeOffset? scheduledAt = null,
        bool storageSupported = true
    )
    {
        // Explicit range checks rather than Enum.IsDefined: these run on every publish, and IsDefined
        // searches the type's cached value table where a contiguous compare suffices.
        if (lane is not (MessageLane.Bus or MessageLane.Queue))
        {
            throw new ArgumentOutOfRangeException(nameof(lane), lane, "A defined messaging lane is required.");
        }

        if (requestedMode is not (DeliveryMode.Durable or DeliveryMode.Direct))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedMode),
                requestedMode,
                "A defined delivery mode is required."
            );
        }

        if (
            enlistment
            is not (
                TransactionEnlistment.WhenAvailable
                or TransactionEnlistment.Required
                or TransactionEnlistment.Never
            )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(enlistment),
                enlistment,
                "A defined transaction enlistment is required."
            );
        }

        if (
            coordinationStatus
            is not (
                DeliveryCoordinationStatus.None
                or DeliveryCoordinationStatus.Compatible
                or DeliveryCoordinationStatus.Incompatible
            )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(coordinationStatus),
                coordinationStatus,
                "Invalid coordination status."
            );
        }

        if (requestedMode is DeliveryMode.Direct && enlistment is TransactionEnlistment.Required)
        {
            throw new InvalidOperationException(
                "Direct delivery cannot require an active unit of work (TransactionEnlistment.Required); durable delivery is required to enlist."
            );
        }

        // Direct bypasses storage and coordination compatibility entirely; every durable request is fenced here,
        // before the capability gate, storage, or transport can run.
        var effectiveStatus = coordinationStatus;
        if (requestedMode is not DeliveryMode.Direct)
        {
            if (!storageSupported)
            {
                throw new MessagingConfigurationException(
                    $"{lane} {requestedMode} delivery requires a matching storage capability contribution."
                );
            }

            if (enlistment is TransactionEnlistment.Never)
            {
                // Never enlists, regardless of what is active — including an incompatible resource.
                effectiveStatus = DeliveryCoordinationStatus.None;
            }
            else if (effectiveStatus is DeliveryCoordinationStatus.Incompatible)
            {
                throw new InvalidOperationException(
                    $"The active unit of work's transaction belongs to another database ({coordination.Mismatch}), "
                        + "so publishing cannot enlist. Use the same database, or TransactionEnlistment.Never for this call."
                );
            }
            else if (effectiveStatus is DeliveryCoordinationStatus.None && enlistment is TransactionEnlistment.Required)
            {
                throw new InvalidOperationException(
                    "Publishing requires an active unit of work (TransactionEnlistment.Required) but none is active in this scope. "
                        + "Begin one with IUnitOfWorkManager.BeginAsync before publishing, or register the message with TransactionEnlistment.WhenAvailable."
                );
            }
        }

        if (delay is not null && scheduledAt is not null)
        {
            throw new ArgumentException(
                "Delay and ScheduledAt are two spellings of the same schedule; supply one, not both.",
                nameof(scheduledAt)
            );
        }

        // Normalized to UTC so the persisted instant is unambiguous regardless of the caller's offset. A past
        // instant is deliberately accepted: not-before semantics make it an already-satisfied constraint, and
        // rejecting it would punish a caller whose computed deadline elapsed between decision and publish.
        // Match PostgreSQL precision while retaining the exact requested instant for frozen middleware options.
        DateTimeOffset? publishAt = scheduledAt?.ToUniversalTime().Floor(TimeSpan.FromMicroseconds(1));
        if (delay is { } value)
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay), value, "Delivery delay must be positive.");
            }

            try
            {
                publishAt = now.Add(value);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(delay),
                    value,
                    "Delivery delay exceeds the supported timestamp range."
                );
            }
        }

        if (requestedMode is DeliveryMode.Direct && delay is not null)
        {
            throw new InvalidOperationException("Direct delivery cannot specify a delay.");
        }

        if (requestedMode is DeliveryMode.Direct && scheduledAt is not null)
        {
            throw new InvalidOperationException("Direct delivery cannot specify a schedule.");
        }

        var path = requestedMode switch
        {
            DeliveryMode.Direct => DeliveryPath.Direct,
            DeliveryMode.Durable when effectiveStatus is DeliveryCoordinationStatus.Compatible =>
                DeliveryPath.DurableCoordinated,
            DeliveryMode.Durable => DeliveryPath.DurableStandalone,
            _ => throw new UnreachableException(),
        };

        // Requested and resolved modes are equal today (no mode is ever rewritten by the matrix), but they are
        // two public headers and two dashboard columns: keep both so a future enlistment-driven resolution can
        // diverge them without changing the wire shape.
        return new DeliveryDecision(
            requestedMode,
            requestedMode,
            enlistment,
            path,
            delay,
            publishAt,
            coordination,
            scheduledAt?.ToUniversalTime()
        );
    }
}
