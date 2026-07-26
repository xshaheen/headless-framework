// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.Internal;

internal enum DeliveryPath
{
    TransportDirect = 0,
    DurableStandalone = 1,
    DurableCoordinated = 2,
}

internal readonly record struct DeliveryDecision(
    MessageLane Lane,
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
    )
    {
        if (!Enum.IsDefined(lane))
        {
            throw new ArgumentOutOfRangeException(nameof(lane), lane, "A defined messaging lane is required.");
        }

        if (!Enum.IsDefined(requestedMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedMode),
                requestedMode,
                "A defined delivery mode is required."
            );
        }

        if (!Enum.IsDefined(coordination.Status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(coordination),
                coordination.Status,
                "Invalid coordination status."
            );
        }

        if (coordination.Status is DeliveryCoordinationStatus.Incompatible)
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

        if (requestedMode is DeliveryMode.TransportDirect && delay is not null)
        {
            throw new InvalidOperationException("TransportDirect delivery cannot specify a delay.");
        }

        var resolvedMode = requestedMode switch
        {
            DeliveryMode.TransportDirect => DeliveryMode.TransportDirect,
            DeliveryMode.Durable => DeliveryMode.Durable,
            DeliveryMode.Auto when delay is not null => DeliveryMode.Durable,
            DeliveryMode.Auto when coordination.Status is DeliveryCoordinationStatus.Compatible => DeliveryMode.Durable,
            DeliveryMode.Auto => DeliveryMode.TransportDirect,
            _ => throw new UnreachableException(),
        };

        var path = resolvedMode switch
        {
            DeliveryMode.TransportDirect => DeliveryPath.TransportDirect,
            DeliveryMode.Durable when coordination.Status is DeliveryCoordinationStatus.Compatible =>
                DeliveryPath.DurableCoordinated,
            DeliveryMode.Durable => DeliveryPath.DurableStandalone,
            _ => throw new UnreachableException(),
        };

        return new DeliveryDecision(lane, requestedMode, resolvedMode, path, delay, publishAt, coordination);
    }
}
