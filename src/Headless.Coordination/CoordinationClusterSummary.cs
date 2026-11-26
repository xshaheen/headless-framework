// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Describes the coordination settings this node runs under, read from its options without I/O.</summary>
[PublicAPI]
public sealed record CoordinationClusterSummary
{
    /// <summary>Gets the cluster this node joins (<see cref="CoordinationOptions.ClusterName"/>).</summary>
    public required string ClusterName { get; init; }

    /// <summary>Gets the membership store provider, such as <c>PostgreSql</c> or <c>Redis</c>.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Gets the role this node registers, or <see langword="null"/> when none is set.</summary>
    public required string? Role { get; init; }

    /// <summary>Gets how often this node beats.</summary>
    public required TimeSpan HeartbeatInterval { get; init; }

    /// <summary>Gets the heartbeat age at which a node reads as suspected.</summary>
    public required TimeSpan SuspicionThreshold { get; init; }

    /// <summary>Gets the heartbeat age at which a node reads as dead.</summary>
    public required TimeSpan DeadThreshold { get; init; }

    /// <summary>Gets the minimum time a dead node's record stays readable.</summary>
    public required TimeSpan DeadRetentionWindow { get; init; }

    /// <summary>
    /// Gets the self-fence margin in effect: <see cref="CoordinationOptions.SelfFenceMargin"/> when set, else its
    /// default.
    /// </summary>
    public required TimeSpan SelfFenceMargin { get; init; }

    /// <summary>
    /// Gets whether this host runs the membership heartbeat. <see langword="false"/> on a host that called
    /// <c>DisableMembershipHeartbeat()</c>, which beats nothing on its own and records no snapshot-derived membership
    /// events.
    /// </summary>
    public required bool MembershipHeartbeatEnabled { get; init; }
}
