// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

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

internal static class DeliveryDecisionResolver
{
    internal static DeliveryDecision Resolve(
        MessageLane lane,
        DeliveryMode requestedMode,
        TimeSpan? delay,
        DeliveryCoordination coordination,
        DateTimeOffset now,
        DateTimeOffset? scheduledAt = null
    ) => Resolve(lane, requestedMode, delay, coordination.Status, now, coordination, scheduledAt);

    // Manually constructed middleware contexts need delivery semantics without live transaction resources.
    internal static DeliveryDecision Resolve(
        MessageLane lane,
        DeliveryMode requestedMode,
        TimeSpan? delay,
        DeliveryCoordinationStatus coordinationStatus,
        DateTimeOffset now,
        DeliveryCoordination coordination = default,
        DateTimeOffset? scheduledAt = null
    )
    {
        // Explicit range checks rather than Enum.IsDefined: these run on every publish, and IsDefined
        // searches the type's cached value table where a contiguous compare suffices.
        if (lane is not (MessageLane.Bus or MessageLane.Queue))
        {
            throw new ArgumentOutOfRangeException(nameof(lane), lane, "A defined messaging lane is required.");
        }

        if (requestedMode is not (DeliveryMode.Auto or DeliveryMode.Durable or DeliveryMode.Direct))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedMode),
                requestedMode,
                "A defined delivery mode is required."
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

        if (coordinationStatus is DeliveryCoordinationStatus.Incompatible && requestedMode is not DeliveryMode.Direct)
        {
            throw new InvalidOperationException(
                $"The active coordination boundary is incompatible with messaging storage ({coordination.Mismatch})."
            );
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

        var resolvedMode = requestedMode switch
        {
            DeliveryMode.Direct => DeliveryMode.Direct,
            DeliveryMode.Durable => DeliveryMode.Durable,
            // publishAt, not delay: an absolute schedule must upgrade Auto to durable the same way a relative
            // delay does, otherwise a scheduled Auto send would resolve to a direct publish with no storage.
            DeliveryMode.Auto when publishAt is not null => DeliveryMode.Durable,
            DeliveryMode.Auto when coordinationStatus is DeliveryCoordinationStatus.Compatible => DeliveryMode.Durable,
            DeliveryMode.Auto => DeliveryMode.Direct,
            _ => throw new UnreachableException(),
        };

        var path = resolvedMode switch
        {
            DeliveryMode.Direct => DeliveryPath.Direct,
            DeliveryMode.Durable when coordinationStatus is DeliveryCoordinationStatus.Compatible =>
                DeliveryPath.DurableCoordinated,
            DeliveryMode.Durable => DeliveryPath.DurableStandalone,
            _ => throw new UnreachableException(),
        };

        return new DeliveryDecision(
            requestedMode,
            resolvedMode,
            path,
            delay,
            publishAt,
            coordination,
            scheduledAt?.ToUniversalTime()
        );
    }
}
