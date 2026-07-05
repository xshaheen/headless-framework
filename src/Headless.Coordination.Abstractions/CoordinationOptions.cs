// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

[PublicAPI]
public sealed class CoordinationOptions
{
    /// <summary>Default Redis / store key prefix applied to all coordination entries.</summary>
    public const string DefaultKeyPrefix = "coordination:";

    /// <summary>Cluster name used when no explicit name is configured.</summary>
    public const string DefaultClusterName = "default";

    /// <summary>
    /// DI key for the <c>IJsonSerializer</c> used to (de)serialize coordination metadata/endpoints. Consumers can
    /// pre-register their own keyed serializer under this key to override coordination's serialization independently
    /// of the global <c>IJsonSerializer</c>.
    /// </summary>
    public const string JsonSerializerServiceKey = "Headless:Coordination:JsonSerializer";

    /// <summary>
    /// Prefix prepended to every coordination key written to the backing store. Changing this value after
    /// data has been written leaves orphaned keys under the old prefix.
    /// </summary>
    public string KeyPrefix { get; set; } = DefaultKeyPrefix;

    /// <summary>
    /// Logical cluster name. Only nodes that share the same cluster name participate in mutual membership
    /// tracking. Must match <c>[A-Za-z0-9._:-]+</c>.
    /// </summary>
    public string ClusterName { get; set; } = DefaultClusterName;

    /// <summary>
    /// Static node identifier to use instead of a runtime-generated one. When <see langword="null"/> the
    /// registered <see cref="INodeIdProvider"/> is invoked. Must not be empty or whitespace when set.
    /// </summary>
    public string? ConfiguredNodeId { get; set; }

    /// <summary>
    /// Optional role label for this node, written to the node descriptor on <see cref="INodeMembership.RegisterAsync"/>.
    /// Roles are informational; the system does not enforce topology based on them.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Arbitrary key/value pairs written to the node descriptor on <see cref="INodeMembership.RegisterAsync"/>.
    /// Useful for service-discovery metadata (e.g. datacenter, version).
    /// </summary>
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How often the heartbeat background service calls <see cref="INodeMembership.HeartbeatAsync"/>.
    /// Must be positive and strictly less than <see cref="SuspicionThreshold"/>.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Elapsed time since the last heartbeat before a node transitions to <see cref="NodeLivenessState.Suspected"/>.
    /// Must be strictly less than <see cref="DeadThreshold"/>.
    /// </summary>
    public TimeSpan SuspicionThreshold { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Elapsed time since the last heartbeat before a node is permanently classified as
    /// <see cref="NodeLivenessState.Dead"/>. Once dead, the incarnation is ineligible for recovery.
    /// </summary>
    public TimeSpan DeadThreshold { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Minimum time dead node records are retained in the backing store before becoming eligible for purge.
    /// Must be at least twice <see cref="HeartbeatInterval"/> so that a reader is guaranteed to see the dead
    /// record at least once before it disappears. This is a floor, not a ceiling: a provider may retain dead
    /// records longer — the relational providers prune shortly after <see cref="DeadThreshold"/>, while the
    /// Redis store keeps them for its <c>RedisKnownNodeRetention</c> (7 days by default). Consumers must
    /// classify by <see cref="NodeLivenessState"/> rather than assume dead records vanish exactly after this
    /// window.
    /// </summary>
    public TimeSpan DeadRetentionWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Action taken when the local process detects that its own membership identity has been lost (i.e.
    /// another node has superseded this incarnation or the store evicted the heartbeat).
    /// </summary>
    public MembershipLostBehavior MembershipLostBehavior { get; set; } = MembershipLostBehavior.StopApplication;
}
