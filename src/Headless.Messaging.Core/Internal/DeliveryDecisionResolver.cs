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
    DeliveryCoordination Coordination
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
        DateTimeOffset now
    ) => Resolve(lane, requestedMode, delay, coordination.Status, now, coordination);

    // Manually constructed middleware contexts need delivery semantics without live transaction resources.
    internal static DeliveryDecision Resolve(
        MessageLane lane,
        DeliveryMode requestedMode,
        TimeSpan? delay,
        DeliveryCoordinationStatus coordinationStatus,
        DateTimeOffset now,
        DeliveryCoordination coordination = default
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

        DateTimeOffset? publishAt = null;
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

        var resolvedMode = requestedMode switch
        {
            DeliveryMode.Direct => DeliveryMode.Direct,
            DeliveryMode.Durable => DeliveryMode.Durable,
            DeliveryMode.Auto when delay is not null => DeliveryMode.Durable,
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

        return new DeliveryDecision(requestedMode, resolvedMode, path, delay, publishAt, coordination);
    }
}
