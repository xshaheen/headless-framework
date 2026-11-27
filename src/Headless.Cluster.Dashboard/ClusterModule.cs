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
    private readonly ClusterEndpoints _cluster;
    private readonly HealthEndpoints _health;
    private readonly ClusterHealthCommands _commands;
    private readonly ClusterSources _sources;
    private readonly NodeChangeSource _changes;

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

        _cluster = new ClusterEndpoints(services);
        _health = new HealthEndpoints(services);
        _commands = new ClusterHealthCommands(Id, services, publisher);
        _sources = new ClusterSources(Id, services);
        _changes = new NodeChangeSource(Id, services, timeProvider, options.ChangePageSize);
    }

    public string Id => ClusterDashboardOptions.ModuleId;

    public string DisplayName => "Cluster";

    public DashboardModuleAssets Assets { get; } = ClusterAssets.Create();

    public IReadOnlyList<DashboardCommand> Commands => _commands.Commands;

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);

        _cluster.Map(endpoints);
        _health.Map(endpoints);
    }

    public async Task<IReadOnlyList<DashboardAttentionItem>> GetAttentionAsync(
        DashboardAttentionRequest request,
        CancellationToken cancellationToken
    )
    {
        return (await _sources.GetAttentionAsync(cancellationToken).ConfigureAwait(false)).Items;
    }

    public Task<DashboardAttentionAnswer> GetAttentionAnswerAsync(
        DashboardAttentionRequest request,
        CancellationToken cancellationToken
    )
    {
        return _sources.GetAttentionAsync(cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(
        DashboardCountsRequest request,
        CancellationToken cancellationToken
    )
    {
        return _sources.GetCountsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<DashboardObject>> ResolveAsync(
        DashboardResolveRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _sources.ResolveAsync(request.Query, cancellationToken);
    }

    public Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedCommandsAsync(
        DashboardAllowedCommandsRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return Task.FromResult(_commands.GetAllowed(request.Targets));
    }

    public Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _commands.ExecuteAsync(request, cancellationToken);
    }

    public Task<string> GetStartCursorAsync(CancellationToken cancellationToken)
    {
        return _changes.GetStartCursorAsync(cancellationToken);
    }

    public Task<DashboardChangeBatch> ReadChangesAsync(string cursor, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(cursor);

        return _changes.ReadChangesAsync(cursor, cancellationToken);
    }
}
