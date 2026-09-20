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

/// <summary>
/// Decides the delivery path before any storage or transport effect. <c>requireCoordination</c> is the receiver's
/// own guarantee, not a caller preference: the outbox surface always requires it and the autonomous surface never
/// does. The matrix:
/// <list type="bullet">
/// <item><c>Durable</c> (default): writes the row inside a compatible active unit of work's transaction, and a
/// standalone durable row when no unit is supplied.</item>
/// <item><c>Direct</c>: transport now, bypassing storage and coordination checks entirely; rejects any schedule
/// and any coordination requirement.</item>
/// </list>
/// A unit the storage cannot join throws whether or not coordination was required, because silently writing
/// outside a transaction the caller believes it is in is the failure this refuses to produce. Requiring
/// coordination with no unit at all also throws, before any effect.
/// </summary>
internal static class DeliveryDecisionResolver
{
    internal static DeliveryDecision Resolve(
        MessageLane lane,
        DeliveryMode requestedMode,
        bool requireCoordination,
        TimeSpan? delay,
        DeliveryCoordination coordination,
        DateTimeOffset now,
        DateTimeOffset? scheduledAt = null,
        bool storageSupported = true,
        string? messageName = null
    ) =>
        Resolve(
            lane,
            requestedMode,
            requireCoordination,
            delay,
            coordination.Status,
            now,
            coordination,
            scheduledAt,
            storageSupported,
            messageName
        );

    // Manually constructed middleware contexts need delivery semantics without live transaction resources.
    // messageName is the declared message type's name; it only sharpens the coordination failure messages.
    internal static DeliveryDecision Resolve(
        MessageLane lane,
        DeliveryMode requestedMode,
        bool requireCoordination,
        TimeSpan? delay,
        DeliveryCoordinationStatus coordinationStatus,
        DateTimeOffset now,
        DeliveryCoordination coordination = default,
        DateTimeOffset? scheduledAt = null,
        bool storageSupported = true,
        string? messageName = null
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

        if (requestedMode is DeliveryMode.Direct && requireCoordination)
        {
            throw new InvalidOperationException(
                "Direct delivery cannot be coordinated with a unit of work; durable delivery is required to write inside its transaction."
            );
        }

        // Direct bypasses storage and coordination compatibility entirely; every durable request is fenced here,
        // before the capability gate, storage, or transport can run.
        if (requestedMode is not DeliveryMode.Direct)
        {
            if (!storageSupported)
            {
                throw new MessagingConfigurationException(
                    $"{lane} {requestedMode} delivery requires a matching storage capability contribution."
                );
            }

            if (coordinationStatus is DeliveryCoordinationStatus.Incompatible)
            {
                // Refused whether or not coordination was required: a unit is active and the storage cannot join
                // it, so a standalone row would survive a rollback the caller expects to discard it.
                throw new InvalidOperationException(_DescribeMismatch(coordination.Mismatch, messageName));
            }

            if (coordinationStatus is DeliveryCoordinationStatus.None && requireCoordination)
            {
                var subject = messageName is null ? "Publishing" : $"Publishing '{messageName}'";

                throw new InvalidOperationException(
                    $"{subject} through the unit-of-work outbox requires an active unit of work, but none was supplied. "
                        + "Begin one with IUnitOfWorkFactory.BeginAsync before publishing, or publish through the autonomous bus."
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
            DeliveryMode.Durable when coordinationStatus is DeliveryCoordinationStatus.Compatible =>
                DeliveryPath.DurableCoordinated,
            DeliveryMode.Durable => DeliveryPath.DurableStandalone,
            _ => throw new UnreachableException(),
        };

        // Requested and resolved modes are equal today (no mode is ever rewritten by the matrix), but they are
        // two public headers and two dashboard columns: keep both so a future coordination-driven resolution can
        // diverge them without changing the wire shape.
        return new DeliveryDecision(
            requestedMode,
            requestedMode,
            path,
            delay,
            publishAt,
            coordination,
            scheduledAt?.ToUniversalTime()
        );
    }

    // The advice has to match the mismatch. A unit that exposes no relational resource is still a unit, so
    // telling its caller to begin one is advice they already followed; they need to hear that the unit they
    // opened has nothing for the messaging storage to write into.
    private static string _DescribeMismatch(DeliveryCoordinationMismatch mismatch, string? messageName)
    {
        var subject = messageName is null ? "Publishing" : $"Publishing '{messageName}'";

        var (detail, advice) = mismatch switch
        {
            DeliveryCoordinationMismatch.MissingRelationalCapability => (
                "the active unit of work exposes no relational resource for the messaging storage to write into",
                "Begin the unit of work over a relational resource for the messaging database, or publish without coordination."
            ),
            DeliveryCoordinationMismatch.TransactionCompleted => (
                "the active unit of work's transaction has already committed or rolled back",
                "Publish before the unit of work completes, or publish without coordination."
            ),
            DeliveryCoordinationMismatch.Database => (
                "the active unit of work's transaction belongs to another database",
                "Use the same database with an open transaction, or publish without coordination."
            ),
            _ => (
                "the active unit of work's transaction belongs to another storage provider",
                "Use the same database with an open transaction, or publish without coordination."
            ),
        };

        return $"{subject} cannot join the active unit of work ({mismatch}): {detail}. {advice}";
    }
}
