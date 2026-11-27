// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination;
using Headless.Dashboard;
using Headless.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Cluster;

/// <summary>
/// The module's shell answers: Needs you, sidebar counts, and id lookup. Health answers come from the monitor's memory;
/// node answers take one liveness snapshot read, so a store that does not answer leaves the health answers standing
/// and is named as the <c>coordination</c> source.
/// </summary>
internal sealed class ClusterSources(string moduleId, ClusterServices services)
{
    /// <summary>The source name a failed snapshot read reports.</summary>
    public const string CoordinationSource = "coordination";

    public const string NodesCount = "nodes";
    public const string ChecksCount = "checks";

    private readonly string _moduleId = Argument.IsNotNullOrWhiteSpace(moduleId);
    private readonly ClusterServices _services = Argument.IsNotNull(services);

    public async Task<DashboardAttentionAnswer> GetAttentionAsync(CancellationToken cancellationToken)
    {
        var items = new List<DashboardAttentionItem>();
        List<string>? unavailable = null;

        if (_services.Monitor is { } monitor)
        {
            items.AddRange(_CheckItems(monitor.GetSnapshot()));
        }

        if (_services.HasCoordination)
        {
            try
            {
                var snapshot = await _services
                    .Membership.GetLivenessSnapshotAsync(cancellationToken)
                    .ConfigureAwait(false);
                items.AddRange(_NodeItems(snapshot));
            }
            catch (Exception exception) when (!_IsCancellation(exception, cancellationToken))
            {
                unavailable = [CoordinationSource];
            }
        }

        return new DashboardAttentionAnswer
        {
            Items =
            [
                .. items
                    .OrderByDescending(static item => item.Severity)
                    .ThenBy(static item => item.Id, StringComparer.Ordinal),
            ],
            UnavailableSources = unavailable ?? [],
        };
    }

    public async Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, DashboardCount>(StringComparer.Ordinal);

        if (_services.Monitor is { } monitor)
        {
            var checks = monitor.GetSnapshot().Checks;
            counts[ChecksCount] = new DashboardCount { Count = checks.Count };
            counts[nameof(HealthStatus.Degraded)] = new DashboardCount
            {
                Count = checks.Count(static check => HealthEndpoints.Latest(check)?.Status == HealthStatus.Degraded),
            };
            counts[nameof(HealthStatus.Unhealthy)] = new DashboardCount
            {
                Count = checks.Count(static check => HealthEndpoints.Latest(check)?.Status == HealthStatus.Unhealthy),
            };
        }

        if (!_services.HasCoordination)
        {
            return counts;
        }

        IReadOnlyList<NodeLivenessSnapshot> snapshot;

        try
        {
            snapshot = await _services.Membership.GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (counts.Count > 0 && !_IsCancellation(exception, cancellationToken))
        {
            // The health counts still answered, so the node entries read as unknown, naming the store, instead of the
            // whole module going stale.
            var unknown = new DashboardCount { Count = 0, UnavailableSources = [CoordinationSource] };
            counts[NodesCount] = unknown;
            counts[nameof(NodeLivenessState.Alive)] = unknown;
            counts[nameof(NodeLivenessState.Suspected)] = unknown;
            counts[nameof(NodeLivenessState.Dead)] = unknown;

            return counts;
        }

        counts[NodesCount] = new DashboardCount { Count = snapshot.Count };
        counts[nameof(NodeLivenessState.Alive)] = _CountState(snapshot, NodeLivenessState.Alive);
        counts[nameof(NodeLivenessState.Suspected)] = _CountState(snapshot, NodeLivenessState.Suspected);
        counts[nameof(NodeLivenessState.Dead)] = _CountState(snapshot, NodeLivenessState.Dead);

        return counts;
    }

    /// <summary>
    /// The objects whose id is <paramref name="query"/>: the exact node incarnation it names, every incarnation of the
    /// node id it names, and the health check it names, matched case-insensitively.
    /// </summary>
    public async Task<IReadOnlyList<DashboardObject>> ResolveAsync(string query, CancellationToken cancellationToken)
    {
        var objects = new List<DashboardObject>();

        if (string.IsNullOrWhiteSpace(query))
        {
            return objects;
        }

        if (_services.HasCoordination)
        {
            var exact = NodeIdentity.TryParse(query, out var identity);

            if (exact || _IsNodeId(query))
            {
                try
                {
                    var snapshot = await _services
                        .Membership.GetLivenessSnapshotAsync(cancellationToken)
                        .ConfigureAwait(false);

                    objects.AddRange(
                        ClusterEndpoints
                            .Order(snapshot)
                            .Where(node =>
                                exact
                                    ? node.Identity == identity
                                    : string.Equals(node.Identity.NodeId.Value, query, StringComparison.Ordinal)
                            )
                            .Select(node => new DashboardObject
                            {
                                Target = ClusterObjects.Ref(
                                    _moduleId,
                                    ClusterObjects.NodeKind,
                                    node.Identity.ToString()
                                ),
                                Title = node.Identity.ToString(),
                                Status = node.State.ToString(),
                            })
                    );
                }
                catch (Exception exception) when (!_IsCancellation(exception, cancellationToken))
                {
                    // Health checks still resolve from memory while the membership store is unreachable.
                }
            }
        }

        if (_services.Monitor is { } monitor && HealthEndpoints.FindCheck(monitor.GetSnapshot(), query) is { } check)
        {
            objects.Add(
                new DashboardObject
                {
                    Target = ClusterObjects.Ref(_moduleId, ClusterObjects.CheckKind, check.Descriptor.Name),
                    Title = check.Descriptor.Name,
                    Status = HealthEndpoints.Latest(check)?.Status.ToString(),
                }
            );
        }

        return objects;
    }

    private IEnumerable<DashboardAttentionItem> _CheckItems(HealthCheckSnapshot snapshot)
    {
        foreach (var check in snapshot.Checks)
        {
            if (HealthEndpoints.Latest(check) is not { Status: HealthStatus.Degraded or HealthStatus.Unhealthy } latest)
            {
                continue;
            }

            var name = check.Descriptor.Name;

            yield return new DashboardAttentionItem
            {
                Id = "cluster.check." + name,
                GroupKey = "cluster.health." + latest.Status,
                Title = $"Health check {name} is {latest.Status}",
                Severity =
                    latest.Status == HealthStatus.Unhealthy
                        ? DashboardAttentionSeverity.Critical
                        : DashboardAttentionSeverity.Warning,
                Kind = DashboardAttentionKind.HealthCheck,
                Count = 1,
                Target = ClusterObjects.Ref(_moduleId, ClusterObjects.CheckKind, name),
                Since = _RunStart(check.Results, latest.Status),
                LastSeenAt = latest.CompletedAt,
            };
        }
    }

    private IEnumerable<DashboardAttentionItem> _NodeItems(IEnumerable<NodeLivenessSnapshot> snapshot)
    {
        foreach (var node in ClusterEndpoints.Order(snapshot))
        {
            if (node.State is not (NodeLivenessState.Suspected or NodeLivenessState.Dead))
            {
                continue;
            }

            var identity = node.Identity.ToString();

            yield return new DashboardAttentionItem
            {
                Id = "cluster.node." + identity,
                GroupKey = "cluster.nodes." + node.State,
                Title = $"Node {identity} is {node.State}",
                Severity =
                    node.State == NodeLivenessState.Dead
                        ? DashboardAttentionSeverity.Critical
                        : DashboardAttentionSeverity.Warning,
                Kind = DashboardAttentionKind.Work,
                Count = 1,
                Target = ClusterObjects.Ref(_moduleId, ClusterObjects.NodeKind, identity),
                Since = node.LastHeartbeatAt,
                LastSeenAt = node.LastHeartbeatAt,
            };
        }
    }

    /// <summary>When the check's current run of <paramref name="status"/> began: its oldest result in that run.</summary>
    private static DateTimeOffset _RunStart(IReadOnlyList<HealthCheckProbeResult> results, HealthStatus status)
    {
        var start = results[^1].CompletedAt;

        for (var index = results.Count - 1; index >= 0 && results[index].Status == status; index--)
        {
            start = results[index].CompletedAt;
        }

        return start;
    }

    private static DashboardCount _CountState(IReadOnlyList<NodeLivenessSnapshot> snapshot, NodeLivenessState state) =>
        new() { Count = snapshot.Count(node => node.State == state) };

    private static bool _IsNodeId(string value)
    {
        try
        {
            _ = new NodeId(value);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool _IsCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
