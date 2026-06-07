// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Coordination;

internal static partial class LoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "GeneratedFallbackNodeId",
        Level = LogLevel.Warning,
        Message = "Generated fallback coordination node id {NodeId}; recovery semantics are degraded because every start is a brand-new node."
    )]
    public static partial void GeneratedFallbackNodeId(this ILogger logger, string nodeId);

    [LoggerMessage(
        EventId = 2,
        EventName = "MembershipEventDropped",
        Level = LogLevel.Warning,
        Message = "Dropped coordination membership event {EventType} for {Identity} because a subscriber is lagging."
    )]
    public static partial void MembershipEventDropped(this ILogger logger, string eventType, NodeIdentity identity);

    [LoggerMessage(
        EventId = 3,
        EventName = "LocalMembershipLost",
        Level = LogLevel.Critical,
        Message = "Local coordination membership identity {Identity} was rejected by the store and is now lost."
    )]
    public static partial void LocalMembershipLost(this ILogger logger, NodeIdentity identity);

    [LoggerMessage(
        EventId = 4,
        EventName = "StopApplicationFailed",
        Level = LogLevel.Error,
        Message = "Failed to stop the host after local coordination membership identity {Identity} was lost."
    )]
    public static partial void StopApplicationFailed(this ILogger logger, Exception exception, NodeIdentity identity);

    [LoggerMessage(
        EventId = 5,
        EventName = "LivenessReadFailed",
        Level = LogLevel.Error,
        Message = "Coordination heartbeat tick failed while reading authoritative liveness; no stale membership events were emitted."
    )]
    public static partial void LivenessReadFailed(this ILogger logger, Exception exception);
}
