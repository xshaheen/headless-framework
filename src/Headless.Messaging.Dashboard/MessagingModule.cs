// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

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
/// With forwarding on, a request for another node's objects goes to that node: commands, Operations, payloads, and
/// reads of the module's API that name the node, by <c>?node=</c> or by the id. Needs you and id lookup cover every
/// node discovery lists, each item labelled with its node, and a node's live changes are relayed while a stream
/// watches its topics. A node that does not answer is named by its state; this node's own data never stands in for
/// it. A call another node forwarded is always served here, at the lower of the serving node's decision and
/// <see cref="MessagingDashboardOptions.ForwardedAccess"/>.
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
        IDashboardOperationRunner,
        IDashboardPeerAccess,
        IDashboardNodeTopicObserver
{
    private readonly MessagingModuleApi _api;
    private readonly MessagingDashboardOptions _options;
    private readonly MessagingForwarding? _forwarding;
    private readonly PeerChangeReader? _peerChanges;

    public MessagingModule(
        MessagingStores stores,
        MessagingMetricsEventListener metrics,
        MessagingDashboardOptions options,
        IServiceProvider services,
        MessagingForwarding? forwarding = null,
        PeerChangeReader? peerChanges = null
    )
    {
        Argument.IsNotNull(stores);
        Argument.IsNotNull(metrics);
        Argument.IsNotNull(options);
        Argument.IsNotNull(services);

        LocalCounts = new MessagingCounts(stores);
        LocalSources = new MessagingSources(stores, LocalCounts, metrics);
        LocalChanges = new MessagingChangeSource(stores, options.ChangePageSize, options.ChangeOverlapWindow);
        _api = new MessagingModuleApi(stores, LocalSources, metrics, services, forwarding);
        LocalCommands = new MessagingCommands(stores, LocalSources, services);
        _options = options;
        _forwarding = forwarding;
        _peerChanges = peerChanges;
    }

    public string PeerAuthenticationScheme => ForwardingProtocol.Scheme;

    public string PeerPolicy => ForwardingProtocol.Scheme;

    internal string NodeName => _options.NodeName;

    internal MessagingSources LocalSources { get; }

    internal MessagingCounts LocalCounts { get; }

    internal MessagingChangeSource LocalChanges { get; }

    internal MessagingCommands LocalCommands { get; }

    // A dashboard-only host reads no store: every answer comes from a discovered node.
    private bool IsStandalone => _forwarding?.IsStandalone == true;

    public void OnNodeTopicWatched(DashboardNodeTopic topic)
    {
        Argument.IsNotNull(topic);

        _peerChanges?.Watch(topic.Node);
    }

    public void OnNodeTopicUnwatched(DashboardNodeTopic topic)
    {
        Argument.IsNotNull(topic);

        _peerChanges?.Unwatch(topic.Node);
    }

    public IReadOnlyList<DashboardCommand> Commands => MessagingCommands.All;

    public Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedCommandsAsync(
        DashboardAllowedCommandsRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        if (MessagingForwarding.IsViewOnly(request.User))
        {
            return Task.FromResult(_ViewOnlyAnswers(request.Targets));
        }

        return _forwarding is null
            ? LocalCommands.GetAllowedAsync(request.Targets, request.User, cancellationToken)
            : _forwarding.GetAllowedAsync(
                request,
                (local, token) => LocalCommands.GetAllowedAsync(local.Targets, local.User, token),
                cancellationToken
            );
    }

    public Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        if (MessagingForwarding.IsViewOnly(request.User))
        {
            var refusal = PeerStates.ViewOnly(_options.NodeName);

            return Task.FromResult(
                new DashboardCommandResult
                {
                    Rows =
                    [
                        .. request.Ids.Select(id => new DashboardRowResult
                        {
                            Id = id,
                            Outcome = DashboardRowOutcome.Skipped,
                            Reason = refusal,
                        }),
                    ],
                    Refusal = refusal,
                }
            );
        }

        return _forwarding is null
            ? LocalCommands.ExecuteAsync(request, cancellationToken)
            : _forwarding.ExecuteAsync(request, LocalCommands.ExecuteAsync, cancellationToken);
    }

    public async Task<DashboardOperationScope> PrepareAsync(
        DashboardOperationScopeRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        if (
            _forwarding is not null
            && await _forwarding
                .PeerOfFilterAsync(request.Filter, request.User, cancellationToken)
                .ConfigureAwait(false)
                is { } peer
        )
        {
            return await _forwarding.PrepareOnPeerAsync(peer, request, cancellationToken).ConfigureAwait(false);
        }

        _ThrowWhenStandalone(nameof(request));

        return await LocalCommands.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DashboardOperationChunk> RunChunkAsync(
        DashboardOperationChunkRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        if (
            _forwarding is not null
            && await _forwarding
                .PeerOfFilterAsync(request.Filter, request.User, cancellationToken)
                .ConfigureAwait(false)
                is { } peer
        )
        {
            return await _forwarding.RunChunkOnPeerAsync(peer, request, cancellationToken).ConfigureAwait(false);
        }

        _ThrowWhenStandalone(nameof(request));

        return await LocalCommands.RunChunkAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public string Id => MessagingDashboardOptions.ModuleId;

    public string DisplayName => "Messaging";

    public DashboardModuleAssets Assets { get; } = MessagingAssets.Create();

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);

        var routes = endpoints.Routes;

        if (_forwarding is null)
        {
            _api.Map(routes, routes);

            return;
        }

        // Reads that name another node go to it; the node list and the peer endpoints are always this node's.
        var relayed = routes.MapGroup(string.Empty);
        relayed.AddEndpointFilter<NodeRelayFilter>();
        _api.Map(relayed, routes);
        PeerEndpoints.Map(routes.MapGroup("/peer"), this);
    }

    public async Task<IReadOnlyList<DashboardAttentionItem>> GetAttentionAsync(
        DashboardAttentionRequest request,
        CancellationToken cancellationToken
    )
    {
        return (await GetAttentionAnswerAsync(request, cancellationToken).ConfigureAwait(false)).Items;
    }

    public async Task<DashboardAttentionAnswer> GetAttentionAnswerAsync(
        DashboardAttentionRequest request,
        CancellationToken cancellationToken
    )
    {
        var local = IsStandalone
            ? new DashboardAttentionAnswer { Items = [] }
            : await LocalSources.GetAttentionAsync(cancellationToken).ConfigureAwait(false);

        return _forwarding is null
            ? local
            : await _forwarding.GetAttentionAsync(local, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DashboardFigure>> GetOverviewAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    )
    {
        return (await GetOverviewAnswerAsync(request, cancellationToken).ConfigureAwait(false)).Figures;
    }

    public async Task<DashboardOverviewAnswer> GetOverviewAnswerAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!IsStandalone)
        {
            return await LocalSources.GetOverviewAsync(cancellationToken).ConfigureAwait(false);
        }

        // A dashboard-only host has no figures of its own: it shows the node it opens on.
        return await _forwarding!
                .ReadFromDefaultAsync<DashboardOverviewAnswer>("/peer/overview", cancellationToken)
                .ConfigureAwait(false)
            ?? new DashboardOverviewAnswer
            {
                Figures = [],
                Tracks = [],
                UnavailableSources = [_NoNodeReachable()],
            };
    }

    public async Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(
        DashboardCountsRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!IsStandalone)
        {
            return await LocalCounts.GetCountsAsync(cancellationToken).ConfigureAwait(false);
        }

        return await _forwarding!
                .ReadFromDefaultAsync<Dictionary<string, DashboardCount>>("/peer/counts", cancellationToken)
                .ConfigureAwait(false)
            ?? new Dictionary<string, DashboardCount>(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<DashboardObject>> ResolveAsync(
        DashboardResolveRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        var local = IsStandalone
            ? []
            : await LocalSources.ResolveAsync(request.Query, cancellationToken).ConfigureAwait(false);

        return _forwarding is null
            ? local
            : await _forwarding
                .ResolveAsync(local, request.Query, request.User, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<DashboardPayload?> GetPayloadAsync(
        DashboardPayloadRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        var target = request.Target;

        if (
            _forwarding is not null
            && await _forwarding
                .PeerForAsync(MessagingForwarding.NodeOf(target.Kind, target.Id), request.User, cancellationToken)
                .ConfigureAwait(false)
                is { } peer
        )
        {
            // The node that holds the message formats it, so its own formatter decides what is shown.
            return await _forwarding.GetPayloadAsync(peer, target, cancellationToken).ConfigureAwait(false);
        }

        return IsStandalone
            ? null
            : await LocalSources.GetPayloadAsync(target, cancellationToken).ConfigureAwait(false);
    }

    public Task<string> GetStartCursorAsync(CancellationToken cancellationToken)
    {
        return LocalChanges.GetStartCursorAsync(cancellationToken);
    }

    public Task<DashboardChangeBatch> ReadChangesAsync(string cursor, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(cursor);

        return LocalChanges.ReadChangesAsync(cursor, cancellationToken);
    }

    // A dashboard-only host has no rows to run an Operation on; the shell answers the refusal with 400 and its text.
    private void _ThrowWhenStandalone(string paramName)
    {
        if (IsStandalone)
        {
            throw new ArgumentException(_NoNodeReachable(), paramName);
        }
    }

    private string _NoNodeReachable() => "No Messaging node reachable. " + _forwarding!.Directory.DiscoveryState;

    // This node caps forwarded calls at view, so every command shows why it is disabled here.
    private IReadOnlyList<DashboardCommandAvailability> _ViewOnlyAnswers(IReadOnlyList<ObjectRef> targets)
    {
        var reason = PeerStates.ViewOnly(_options.NodeName);

        return
        [
            .. targets.SelectMany(target =>
                MessagingCommands
                    .All.Where(command => string.Equals(command.ObjectKind, target.Kind, StringComparison.Ordinal))
                    .Select(command => new DashboardCommandAvailability
                    {
                        Target = target,
                        CommandId = command.Id,
                        Allowed = false,
                        DisabledReason = reason,
                    })
            ),
        ];
    }
}
