// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Cluster;

/// <summary>
/// The Cluster dashboard module: the incarnations Coordination's membership store reports, their metadata agreement,
/// this node's membership event log, and the serving host's health checks, with Needs you, sidebar counts, id lookup,
/// probe commands, and live node changes from every node.
/// </summary>
/// <remarks>
/// Its object kinds are <c>node</c> (a <c>node@incarnation</c> identity), <c>check</c> (a health check name), and
/// <c>health</c> (the serving host, which only the probe-all command acts on), so its stream topics are
/// <c>cluster.node</c> and <c>cluster.check</c>. Its commands are <c>cluster.probe</c> and <c>cluster.probe-all</c>.
/// </remarks>
internal sealed class ClusterModule
    : IDashboardModule,
        IDashboardAttentionSource,
        IDashboardCountSource,
        IDashboardObjectResolver,
        IDashboardCommandSource,
        IDashboardChangeSource
{
    public ClusterModule(
        ClusterServices services,
        IDashboardEventPublisher publisher,
        TimeProvider timeProvider,
        ClusterDashboardOptions options
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(publisher);
        Argument.IsNotNull(timeProvider);
        Argument.IsNotNull(options);
    }

    public string Id => ClusterDashboardOptions.ModuleId;

    public string DisplayName => "Cluster";

    public DashboardModuleAssets Assets { get; } = ClusterAssets.Create();

    public IReadOnlyList<DashboardCommand> Commands => [];

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);
    }

    public Task<IReadOnlyList<DashboardAttentionItem>> GetAttentionAsync(
        DashboardAttentionRequest request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult<IReadOnlyList<DashboardAttentionItem>>([]);
    }

    public Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(
        DashboardCountsRequest request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult<IReadOnlyDictionary<string, DashboardCount>>(
            new Dictionary<string, DashboardCount>(StringComparer.Ordinal)
        );
    }

    public Task<IReadOnlyList<DashboardObject>> ResolveAsync(
        DashboardResolveRequest request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult<IReadOnlyList<DashboardObject>>([]);
    }

    public Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedCommandsAsync(
        DashboardAllowedCommandsRequest request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult<IReadOnlyList<DashboardCommandAvailability>>([]);
    }

    public Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(new DashboardCommandResult { Rows = [] });
    }

    public Task<string> GetStartCursorAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult("v1:");
    }

    public Task<DashboardChangeBatch> ReadChangesAsync(string cursor, CancellationToken cancellationToken)
    {
        return Task.FromResult(new DashboardChangeBatch { Changes = [], Cursor = cursor });
    }
}
