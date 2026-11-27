// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Cluster;

/// <summary>The serializer for every Cluster module response, generated so no response reflects at run time.</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(ClusterSummaryView))]
[JsonSerializable(typeof(ClusterNodeList))]
[JsonSerializable(typeof(ClusterNodeDetail))]
[JsonSerializable(typeof(ClusterAgreementList))]
[JsonSerializable(typeof(ClusterEventList))]
[JsonSerializable(typeof(ClusterHealthView))]
[JsonSerializable(typeof(ClusterCheckList))]
[JsonSerializable(typeof(ClusterCheckDetail))]
[JsonSerializable(typeof(ClusterCheckView))]
[JsonSerializable(typeof(ClusterNodeView))]
internal sealed partial class ClusterDashboardJsonContext : JsonSerializerContext;

/// <summary>Which halves the serving host has, and the coordination settings it runs under.</summary>
/// <param name="Host">The serving host's label: its node identity when it is a member, else its machine name.</param>
/// <param name="Coordination">The coordination settings, or <see langword="null"/> without Coordination.</param>
/// <param name="Health">The health checks' registration, or <see langword="null"/> without health checks.</param>
internal sealed record ClusterSummaryView(
    string Host,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ClusterCoordinationView? Coordination,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ClusterHealthRegistrationView? Health
);

internal sealed record ClusterCoordinationView(
    string ClusterName,
    string ProviderName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Role,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? LocalNode,
    long HeartbeatIntervalMilliseconds,
    long SuspicionThresholdMilliseconds,
    long DeadThresholdMilliseconds,
    long DeadRetentionWindowMilliseconds,
    long SelfFenceMarginMilliseconds,
    bool MembershipHeartbeatEnabled
)
{
    public static ClusterCoordinationView From(CoordinationClusterSummary summary, NodeIdentity? local) =>
        new(
            summary.ClusterName,
            summary.ProviderName,
            summary.Role,
            local?.ToString(),
            (long)summary.HeartbeatInterval.TotalMilliseconds,
            (long)summary.SuspicionThreshold.TotalMilliseconds,
            (long)summary.DeadThreshold.TotalMilliseconds,
            (long)summary.DeadRetentionWindow.TotalMilliseconds,
            (long)summary.SelfFenceMargin.TotalMilliseconds,
            summary.MembershipHeartbeatEnabled
        );
}

/// <summary>Whether the serving host registered health checks and the monitor that records them.</summary>
/// <param name="Registered">Always <see langword="true"/>: without health checks the summary carries no object.</param>
/// <param name="Monitor">Whether the health check monitor, which the Health view reads, is registered.</param>
internal sealed record ClusterHealthRegistrationView(bool Registered, bool Monitor);

/// <summary>One incarnation of the liveness snapshot.</summary>
internal sealed record ClusterNodeView(
    string Id,
    string NodeId,
    long Incarnation,
    NodeLivenessState State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Role,
    DateTimeOffset LastHeartbeatAt,
    bool IsLocal,
    string OwnerTag,
    IReadOnlyDictionary<string, string> Metadata
)
{
    public static ClusterNodeView From(NodeLivenessSnapshot node, string clusterName, NodeIdentity? local) =>
        new(
            node.Identity.ToString(),
            node.Identity.NodeId.Value,
            node.Identity.Incarnation.Value,
            node.State,
            node.Role,
            node.LastHeartbeatAt,
            local == node.Identity,
            NodeOwnerTag.Format(clusterName, node.Identity),
            new SortedDictionary<string, string>(node.Metadata.ToDictionary(), StringComparer.Ordinal)
        );
}

internal sealed record ClusterNodeList(IReadOnlyList<ClusterNodeView> Items);

/// <summary>One node with how the live nodes agree on each of its keys, and this node's events about it.</summary>
internal sealed record ClusterNodeDetail(
    ClusterNodeView Node,
    IReadOnlyList<ClusterNodeAgreementView> Agreement,
    IReadOnlyList<ClusterEventView> Events
);

/// <summary>How the live nodes agree on one key the node carries.</summary>
/// <param name="Value">The node's own value.</param>
/// <param name="Matches">How many compared nodes hold the node's value, the node itself included when it is live.</param>
/// <param name="Compared">How many live nodes were compared.</param>
/// <param name="Groups">The groups of compared nodes that hold another value, or lack the key.</param>
internal sealed record ClusterNodeAgreementView(
    string Key,
    bool IsUnanimous,
    string Value,
    int Matches,
    int Compared,
    IReadOnlyList<ClusterValueGroupView> Groups
);

internal sealed record ClusterValueGroupView(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Value,
    IReadOnlyList<string> Nodes
)
{
    public static ClusterValueGroupView From(NodeMetadataValueGroup group) =>
        new(group.Value, [.. group.Nodes.Select(static node => node.ToString())]);
}

internal sealed record ClusterAgreementKeyView(
    string Key,
    bool IsUnanimous,
    IReadOnlyList<ClusterValueGroupView> Groups
);

internal sealed record ClusterAgreementList(IReadOnlyList<ClusterAgreementKeyView> Keys);

/// <summary>One membership event this node published.</summary>
/// <param name="Kind">The event's type name, such as <c>NodeSuspected</c>.</param>
internal sealed record ClusterEventView(
    long Sequence,
    string Kind,
    string Node,
    DateTimeOffset ObservedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] NodeLivenessState? State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? LastHeartbeatAt,
    bool IsFirstObservation
)
{
    public static ClusterEventView From(MembershipEventLogEntry entry) =>
        new(
            entry.Sequence,
            entry.Event.GetType().Name,
            entry.Event.Identity.ToString(),
            entry.ObservedAt,
            entry.State,
            entry.LastHeartbeatAt,
            entry.IsFirstObservation
        );
}

/// <summary>This node's membership event log, newest first.</summary>
/// <param name="Observer">The node whose log this is.</param>
/// <param name="Clock">Always <c>local</c>: the times come from the observing node's clock.</param>
internal sealed record ClusterEventList(string Observer, string Clock, IReadOnlyList<ClusterEventView> Items);

/// <summary>The serving host's health at a glance.</summary>
/// <param name="Status">The worst latest status, or <see langword="null"/> before the first probe.</param>
/// <param name="ProbedAt">When the latest recorded probe completed, or <see langword="null"/> before the first.</param>
/// <param name="Liveness">Liveness checks whose latest result is healthy, or <see langword="null"/> with none.</param>
/// <param name="Slowest">The check whose latest probe took longest, or <see langword="null"/> before the first.</param>
internal sealed record ClusterHealthView(
    string Host,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] HealthStatus? Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? ProbedAt,
    ClusterHealthTotals Totals,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ClusterLivenessView? Liveness,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ClusterSlowestView? Slowest
);

internal sealed record ClusterHealthTotals(int Healthy, int Degraded, int Unhealthy, int NotProbed);

internal sealed record ClusterLivenessView(int Healthy, int Total);

internal sealed record ClusterSlowestView(string Name, long DurationMilliseconds);

/// <summary>One health check and its latest result.</summary>
/// <param name="Latest">The latest recorded result, or <see langword="null"/> before the check was first probed.</param>
internal sealed record ClusterCheckView(
    string Name,
    IReadOnlyList<string> Tags,
    HealthStatus FailureStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? TimeoutMilliseconds,
    bool IsLiveness,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ClusterProbeResultView? Latest
)
{
    public static ClusterCheckView From(HealthCheckHistory history) =>
        new(
            history.Descriptor.Name,
            history.Descriptor.Tags,
            history.Descriptor.FailureStatus,
            history.Descriptor.Timeout is { } timeout ? (long)timeout.TotalMilliseconds : null,
            history.Descriptor.IsLiveness,
            HealthEndpoints.Latest(history) is { } latest ? ClusterProbeResultView.From(latest) : null
        );
}

internal sealed record ClusterProbeResultView(
    HealthStatus Status,
    long DurationMilliseconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Description,
    DateTimeOffset CompletedAt
)
{
    public static ClusterProbeResultView From(HealthCheckProbeResult result) =>
        new(result.Status, (long)result.Duration.TotalMilliseconds, result.Description, result.CompletedAt);
}

internal sealed record ClusterCheckList(IReadOnlyList<ClusterCheckView> Items);

/// <summary>One health check with its recent results.</summary>
/// <param name="Results">Up to the last 30 results, newest first.</param>
internal sealed record ClusterCheckDetail(ClusterCheckView Check, IReadOnlyList<ClusterProbeResultView> Results);
