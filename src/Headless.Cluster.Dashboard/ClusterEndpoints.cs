// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination;
using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Headless.Cluster;

/// <summary>
/// The Cluster module's membership reads at <c>{base}/api/cluster</c>: the summary of both halves, the incarnations of
/// the liveness snapshot, one node with its agreement and events, this node's event log, and metadata agreement. Each
/// request makes at most one snapshot read.
/// </summary>
internal sealed class ClusterEndpoints(ClusterServices services)
{
    internal const string MissingCoordination =
        "Coordination is not registered on this host. Call services.AddHeadlessCoordination(...) with a provider to "
        + "show cluster membership.";

    private readonly ClusterServices _services = Argument.IsNotNull(services);

    public void Map(DashboardModuleEndpoints endpoints)
    {
        var routes = endpoints.Routes;

        routes.MapGet("/summary", _Summary);
        routes.MapGet("/nodes", _ListNodesAsync);
        routes.MapGet("/nodes/{id}", _GetNodeAsync);
        routes.MapGet("/events", _Events);
        routes.MapGet("/metadata-agreement", _AgreementAsync);
    }

    private JsonHttpResult<ClusterSummaryView> _Summary()
    {
        var coordination = _services.HasCoordination
            ? ClusterCoordinationView.From(_services.Summary, _services.LocalIdentity)
            : null;
        var health = _services.HasHealthChecks
            ? new ClusterHealthRegistrationView(Registered: true, Monitor: _services.Monitor is not null)
            : null;

        return TypedResults.Json(
            new ClusterSummaryView(_services.HostLabel, coordination, health),
            ClusterDashboardJsonContext.Default.ClusterSummaryView
        );
    }

    private async Task<Results<JsonHttpResult<ClusterNodeList>, ValidationProblem, ProblemHttpResult>> _ListNodesAsync(
        string? state,
        CancellationToken cancellationToken
    )
    {
        if (!_services.HasCoordination)
        {
            return _NotRegistered();
        }

        NodeLivenessState? filter = null;

        if (!string.IsNullOrEmpty(state))
        {
            if (!DashboardValidation.TryParseEnumName<NodeLivenessState>(state, out var parsed))
            {
                return DashboardValidation.Problem(
                    "state",
                    DashboardValidation.EnumRule,
                    "state must be Alive, Suspected, or Dead."
                );
            }

            filter = parsed;
        }

        var snapshot = await _services.Membership.GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var clusterName = _services.Summary.ClusterName;
        var local = _services.LocalIdentity;

        return TypedResults.Json(
            new ClusterNodeList([
                .. Order(snapshot)
                    .Where(node => filter is null || node.State == filter)
                    .Select(node => ClusterNodeView.From(node, clusterName, local)),
            ]),
            ClusterDashboardJsonContext.Default.ClusterNodeList
        );
    }

    private async Task<Results<JsonHttpResult<ClusterNodeDetail>, NotFound, ProblemHttpResult>> _GetNodeAsync(
        string id,
        CancellationToken cancellationToken
    )
    {
        if (!_services.HasCoordination)
        {
            return _NotRegistered();
        }

        if (!NodeIdentity.TryParse(id, out var identity))
        {
            return TypedResults.NotFound();
        }

        var snapshot = await _services.Membership.GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (snapshot.FirstOrDefault(node => node.Identity == identity) is not { } node)
        {
            return TypedResults.NotFound();
        }

        var events = (_services.EventLog?.GetEntries() ?? [])
            .Where(entry => entry.Event.Identity == identity)
            .Reverse()
            .Select(ClusterEventView.From)
            .ToList();

        return TypedResults.Json(
            new ClusterNodeDetail(
                ClusterNodeView.From(node, _services.Summary.ClusterName, _services.LocalIdentity),
                _NodeAgreement(snapshot, node),
                events
            ),
            ClusterDashboardJsonContext.Default.ClusterNodeDetail
        );
    }

    private Results<JsonHttpResult<ClusterEventList>, ProblemHttpResult> _Events()
    {
        if (!_services.HasCoordination)
        {
            return _NotRegistered();
        }

        var entries = _services.EventLog?.GetEntries() ?? [];

        return TypedResults.Json(
            new ClusterEventList(_services.HostLabel, "local", [.. entries.Reverse().Select(ClusterEventView.From)]),
            ClusterDashboardJsonContext.Default.ClusterEventList
        );
    }

    private async Task<Results<JsonHttpResult<ClusterAgreementList>, ProblemHttpResult>> _AgreementAsync(
        CancellationToken cancellationToken
    )
    {
        if (!_services.HasCoordination)
        {
            return _NotRegistered();
        }

        var snapshot = await _services.Membership.GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Json(
            new ClusterAgreementList([
                .. LiveKeys(snapshot)
                    .Select(key => NodeMetadataAgreement.Compare(snapshot, key))
                    .Select(static result => new ClusterAgreementKeyView(
                        result.Key,
                        result.IsUnanimous,
                        [.. result.Groups.Select(ClusterValueGroupView.From)]
                    )),
            ]),
            ClusterDashboardJsonContext.Default.ClusterAgreementList
        );
    }

    /// <summary>The snapshot in a stable order: by node id, then incarnation.</summary>
    internal static IEnumerable<NodeLivenessSnapshot> Order(IEnumerable<NodeLivenessSnapshot> snapshot) =>
        snapshot
            .OrderBy(static node => node.Identity.NodeId.Value, StringComparer.Ordinal)
            .ThenBy(static node => node.Identity.Incarnation.Value);

    /// <summary>
    /// Every key an alive or suspected node carries, ordinally sorted. A dead node's keys are left out: agreement
    /// compares only the nodes still serving.
    /// </summary>
    internal static IReadOnlyList<string> LiveKeys(IEnumerable<NodeLivenessSnapshot> snapshot) =>
        [
            .. snapshot
                .Where(static node => node.State is NodeLivenessState.Alive or NodeLivenessState.Suspected)
                .SelectMany(static node => node.Metadata.Keys)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    private static List<ClusterNodeAgreementView> _NodeAgreement(
        IReadOnlyList<NodeLivenessSnapshot> snapshot,
        NodeLivenessSnapshot node
    )
    {
        var agreement = new List<ClusterNodeAgreementView>(node.Metadata.Count);

        foreach (var (key, value) in node.Metadata.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var result = NodeMetadataAgreement.Compare(snapshot, key);
            var own = result.Groups.FirstOrDefault(group =>
                string.Equals(group.Value, value, StringComparison.Ordinal)
            );

            agreement.Add(
                new ClusterNodeAgreementView(
                    key,
                    result.IsUnanimous,
                    value,
                    own?.Nodes.Count ?? 0,
                    result.Groups.Sum(static group => group.Nodes.Count),
                    [.. result.Groups.Where(group => !ReferenceEquals(group, own)).Select(ClusterValueGroupView.From)]
                )
            );
        }

        return agreement;
    }

    private static ProblemHttpResult _NotRegistered() =>
        TypedResults.Problem(
            detail: MissingCoordination,
            statusCode: StatusCodes.Status404NotFound,
            title: "Coordination is not registered"
        );
}
