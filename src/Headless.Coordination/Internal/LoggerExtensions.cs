// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Coordination;

internal static partial class LoggerExtensions
{
    // EventId 1 was the generated-node-id warning; host-name discovery now lives in Headless.Core, which logs it.

    [LoggerMessage(
        EventId = 2,
        EventName = "MembershipEventDropped",
        Level = LogLevel.Warning,
        Message = "Dropped coordination membership event {EventType} for {Identity} because a subscriber is lagging."
    )]
    internal static partial void MembershipEventDropped(this ILogger logger, string eventType, NodeIdentity identity);

    [LoggerMessage(
        EventId = 3,
        EventName = "LocalMembershipLost",
        Level = LogLevel.Critical,
        Message = "Local coordination membership identity {Identity} was rejected by the store and is now lost."
    )]
    internal static partial void LocalMembershipLost(this ILogger logger, NodeIdentity identity);

    [LoggerMessage(
        EventId = 4,
        EventName = "StopApplicationFailed",
        Level = LogLevel.Error,
        Message = "Failed to stop the host after local coordination membership identity {Identity} was lost."
    )]
    internal static partial void StopApplicationFailed(this ILogger logger, Exception exception, NodeIdentity identity);

    [LoggerMessage(
        EventId = 5,
        EventName = "LivenessReadFailed",
        Level = LogLevel.Error,
        Message = "Coordination heartbeat tick failed while reading authoritative liveness; no stale membership events were emitted."
    )]
    internal static partial void LivenessReadFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 6,
        EventName = "MembershipRegistrationRetry",
        Level = LogLevel.Warning,
        Message = "Coordination membership registration attempt {Attempt}/{MaxAttempts} failed; retrying with backoff."
    )]
    internal static partial void MembershipRegistrationRetry(
        this ILogger logger,
        Exception exception,
        int attempt,
        int maxAttempts
    );

    [LoggerMessage(
        EventId = 7,
        EventName = "LeaveOnShutdownTimedOut",
        Level = LogLevel.Warning,
        Message = "Coordination graceful leave timed out during host shutdown; the store will reclaim the membership after the dead threshold."
    )]
    internal static partial void LeaveOnShutdownTimedOut(this ILogger logger);

    [LoggerMessage(
        EventId = 8,
        EventName = "LeaveOnShutdownFailed",
        Level = LogLevel.Error,
        Message = "Coordination graceful leave failed during host shutdown; the store will reclaim the membership after the dead threshold."
    )]
    internal static partial void LeaveOnShutdownFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 9,
        EventName = "MembershipRegistrationFailed",
        Level = LogLevel.Error,
        Message = "Coordination membership registration failed after {MaxAttempts} attempts; stopping the membership loop without stopping the host."
    )]
    internal static partial void MembershipRegistrationFailed(
        this ILogger logger,
        Exception exception,
        int maxAttempts
    );

    [LoggerMessage(
        EventId = 10,
        EventName = "HeartbeatFailed",
        Level = LogLevel.Error,
        Message = "Coordination heartbeat for {Identity} failed with {Remaining} of fence budget left; the node self-fences {FenceBudget} after its last confirmed store write."
    )]
    internal static partial void HeartbeatFailed(
        this ILogger logger,
        Exception exception,
        NodeIdentity identity,
        TimeSpan remaining,
        TimeSpan fenceBudget
    );

    [LoggerMessage(
        EventId = 11,
        EventName = "SelfFenceFailed",
        Level = LogLevel.Error,
        Message = "Coordination self-fence for {Identity} failed while signalling local membership loss."
    )]
    internal static partial void SelfFenceFailed(this ILogger logger, Exception exception, NodeIdentity identity);

    [LoggerMessage(
        EventId = 12,
        EventName = "DrainWaitCutShort",
        Level = LogLevel.Warning,
        Message = "Coordination stopped waiting for drain participants because the host stop deadline arrived; the node leaves membership while work may still be settling."
    )]
    internal static partial void DrainWaitCutShort(this ILogger logger);

    [LoggerMessage(
        EventId = 13,
        EventName = "DrainParticipantFailed",
        Level = LogLevel.Error,
        Message = "Coordination drain participant {Participant} failed during host shutdown; the node stops waiting for it."
    )]
    internal static partial void DrainParticipantFailed(this ILogger logger, Exception exception, string participant);
}
