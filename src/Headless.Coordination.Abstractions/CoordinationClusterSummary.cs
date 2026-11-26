// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Describes the coordination settings this node runs under, read from its options without I/O.</summary>
/// <param name="ClusterName">The cluster this node joins (<see cref="CoordinationOptions.ClusterName"/>).</param>
/// <param name="ProviderName">The membership store provider, such as <c>PostgreSql</c> or <c>Redis</c>.</param>
/// <param name="Role">The role this node registers, or <see langword="null"/> when none is set.</param>
/// <param name="HeartbeatInterval">How often this node beats.</param>
/// <param name="SuspicionThreshold">The heartbeat age at which a node reads as suspected.</param>
/// <param name="DeadThreshold">The heartbeat age at which a node reads as dead.</param>
/// <param name="DeadRetentionWindow">The minimum time a dead node's record stays readable.</param>
/// <param name="SelfFenceMargin">
/// The self-fence margin in effect: <see cref="CoordinationOptions.SelfFenceMargin"/> when set, else its default.
/// </param>
/// <param name="MembershipHeartbeatEnabled">
/// <see langword="false"/> on a host that called <c>DisableMembershipHeartbeat()</c>, which beats nothing on its own
/// and records no snapshot-derived membership events.
/// </param>
[PublicAPI]
public sealed record CoordinationClusterSummary(
    string ClusterName,
    string ProviderName,
    string? Role,
    TimeSpan HeartbeatInterval,
    TimeSpan SuspicionThreshold,
    TimeSpan DeadThreshold,
    TimeSpan DeadRetentionWindow,
    TimeSpan SelfFenceMargin,
    bool MembershipHeartbeatEnabled
);
