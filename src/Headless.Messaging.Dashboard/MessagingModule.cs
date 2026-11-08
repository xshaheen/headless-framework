// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The Messaging module of the unified dashboard: published and received messages of every Messaging store, with
/// Needs you, Overview figures, sidebar counts, id lookup, payloads, live changes from every node, and the operator
/// commands and Operations over them.
/// </summary>
/// <remarks>
/// <para>
/// Its object kinds are <c>published</c> and <c>received</c>, so its stream topics are <c>messaging.published</c> and
/// <c>messaging.received</c>, plus <c>circuit</c> and <c>node</c>, which only commands target. An object id is
/// <c>{node}~{store}~{storage id}</c> (see <see cref="MessagingViews"/>). Commands are listed on
/// <see cref="MessagingCommands"/>.
/// The module reads the main store and every additional outbox; a store that fails or is slow is named in the
/// answers' unavailable sources while the others answer, so the module is never marked stale for one store. Its sidebar
/// count keys are listed on <see cref="MessagingCounts"/>, and its own API is mapped by <see cref="MessagingModuleApi"/>.
/// </para>
/// <para>
/// This node answers for itself only. Every id already names its node, so a later version can forward a request for
/// another node's id to that node.
/// </para>
/// </remarks>
internal sealed class MessagingModule
    : IDashboardModule,
        IDashboardAttentionSource,
        IDashboardOverviewSource,
        IDashboardCountSource,
        IDashboardObjectResolver,
        IDashboardPayloadSource,
        IDashboardChangeSource,
        IDashboardCommandSource,
        IDashboardOperationRunner
{
    private readonly MessagingSources _sources;
    private readonly MessagingCounts _counts;
    private readonly MessagingChangeSource _changes;
    private readonly MessagingModuleApi _api;
    private readonly MessagingCommands _commands;

    public MessagingModule(
        MessagingStores stores,
        MessagingMetricsEventListener metrics,
        MessagingDashboardOptions options,
        IServiceProvider services
    )
    {
        Argument.IsNotNull(stores);
        Argument.IsNotNull(metrics);
        Argument.IsNotNull(options);
        Argument.IsNotNull(services);

        _counts = new MessagingCounts(stores);
        _sources = new MessagingSources(stores, _counts, metrics);
        _changes = new MessagingChangeSource(stores, options.ChangePageSize, options.ChangeOverlapWindow);
        _api = new MessagingModuleApi(stores, _sources, metrics, services);
        _commands = new MessagingCommands(stores, _sources, services);
    }

    public IReadOnlyList<DashboardCommand> Commands => MessagingCommands.All;

    public Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedCommandsAsync(
        DashboardAllowedCommandsRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _commands.GetAllowedAsync(request.Targets, request.User, cancellationToken);
    }

    public Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _commands.ExecuteAsync(request, cancellationToken);
    }

    public Task<DashboardOperationScope> PrepareAsync(
        DashboardOperationScopeRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _commands.PrepareAsync(request, cancellationToken);
    }

    public Task<DashboardOperationChunk> RunChunkAsync(
        DashboardOperationChunkRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _commands.RunChunkAsync(request, cancellationToken);
    }

    public string Id => MessagingDashboardOptions.ModuleId;

    public string DisplayName => "Messaging";

    public DashboardModuleAssets Assets { get; } = MessagingAssets.Create();

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);

        _api.Map(endpoints.Routes);
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

    public async Task<IReadOnlyList<DashboardFigure>> GetOverviewAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    )
    {
        return (await _sources.GetOverviewAsync(cancellationToken).ConfigureAwait(false)).Figures;
    }

    public Task<DashboardOverviewAnswer> GetOverviewAnswerAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    )
    {
        return _sources.GetOverviewAsync(cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(
        DashboardCountsRequest request,
        CancellationToken cancellationToken
    )
    {
        return _counts.GetCountsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<DashboardObject>> ResolveAsync(
        DashboardResolveRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _sources.ResolveAsync(request.Query, cancellationToken);
    }

    public Task<DashboardPayload?> GetPayloadAsync(DashboardPayloadRequest request, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(request);

        return _sources.GetPayloadAsync(request.Target, cancellationToken);
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
