// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Defines shared options for coordination: cluster identity, key prefixing, heartbeat and
/// liveness thresholds, dead-record retention, and behavior when the local node loses membership.
/// Provider packages layer their own options on top.
/// </summary>
[PublicAPI]
public sealed class CoordinationOptions
{
    /// <summary>Default store key prefix applied to all coordination entries.</summary>
    public const string DefaultKeyPrefix = "coordination:";

    /// <summary>
    /// Gets the maximum cluster name length in characters. The value is combined with the node identifier
    /// to form the key, sized with <see cref="NodeId.MaxLength"/> to fit within the 900-byte clustered index limit of SQL Server.
    /// </summary>
    public const int ClusterNameMaxLength = 128;

    /// <summary>Gets the default cluster name used when no explicit name is configured.</summary>
    public const string DefaultClusterName = "default";

    /// <summary>
    /// Gets the dependency injection key for the <c>IJsonSerializer</c> used to serialize and deserialize coordination metadata
    /// and endpoints. Consumers can register a keyed serializer under this key to override coordination serialization
    /// independently of the global serializer.
    /// </summary>
    public const string JsonSerializerServiceKey = "Headless:Coordination:JsonSerializer";

    /// <summary>
    /// Gets or sets the prefix prepended to every coordination key written to the backing store.
    /// Changing this value after data is written leaves orphaned keys under the previous prefix.
    /// </summary>
    public string KeyPrefix { get; set; } = DefaultKeyPrefix;

    /// <summary>
    /// Gets or sets the logical cluster name. Only nodes that share the cluster name participate in mutual
    /// membership tracking. Must match <c>[A-Za-z0-9._:-]+</c> and not exceed <see cref="ClusterNameMaxLength"/> characters.
    /// </summary>
    public string ClusterName { get; set; } = DefaultClusterName;

    /// <summary>
    /// Gets or sets the optional role label for this node, written to the node descriptor during <see cref="INodeMembership.RegisterAsync"/>.
    /// Roles are informational; the system does not enforce topology based on roles.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Gets arbitrary key and value metadata pairs written to the node descriptor during <see cref="INodeMembership.RegisterAsync"/>,
    /// such as datacenter or version information.
    /// </summary>
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets how often the heartbeat background service calls <see cref="INodeMembership.HeartbeatAsync"/>.
    /// Must be positive and less than <see cref="SuspicionThreshold"/>.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the elapsed time since the last heartbeat before a node transitions to <see cref="NodeLivenessState.Suspected"/>.
    /// Must be less than <see cref="DeadThreshold"/>.
    /// </summary>
    public TimeSpan SuspicionThreshold { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets or sets the elapsed time since the last heartbeat before a node is permanently classified as
    /// <see cref="NodeLivenessState.Dead"/>. Once dead, the incarnation is ineligible for recovery.
    /// </summary>
    public TimeSpan DeadThreshold { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the minimum duration dead node records are retained in the backing store before becoming
    /// eligible for removal. Must be at least twice <see cref="HeartbeatInterval"/>, so a reader is guaranteed
    /// to see the dead record at least once before it disappears. This value is a floor, not a ceiling:
    /// a provider may retain dead records longer. The relational providers prune shortly after
    /// <see cref="DeadThreshold"/>, while the Redis store keeps records for its <c>RedisKnownNodeRetention</c>
    /// (7 days by default). Consumers must classify by <see cref="NodeLivenessState"/> rather than assume dead
    /// records are removed immediately after this window.
    /// </summary>
    public TimeSpan DeadRetentionWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the action taken when the local process detects that its own membership identity is lost,
    /// such as when another node supersedes the incarnation or the store evicts the heartbeat.
    /// </summary>
    public MembershipLostBehavior MembershipLostBehavior { get; set; } = MembershipLostBehavior.StopApplication;
}
