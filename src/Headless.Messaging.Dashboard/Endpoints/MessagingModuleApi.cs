// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Security.Claims;
using Headless.Checks;
using Headless.Dashboard;
using Headless.Hosting;
using Headless.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The Messaging module's own API at <c>{base}/api/messaging</c>: published and received lists over every store,
/// message detail, the generations of a received message, scheduled deliveries, the operator ledger, consumers, contracts,
/// and providers from the host manifest, circuit breakers, each store's backlog, nodes, and this node's real-time
/// throughput. Every call is a read, so a viewer without
/// a named operator can make it.
/// </summary>
/// <remarks>
/// Ids are <c>{node}~{store}~{storage id}</c>. Without forwarding this node answers only for itself, so an id naming
/// another node is <c>404</c>; with forwarding, a read that names another node by <c>?node=</c> or by its id is
/// answered by that node (see <see cref="NodeRelayFilter"/>). A list that covers several stores names the stores that did not answer in <c>unavailable</c>.
/// </remarks>
internal sealed class MessagingModuleApi(
    MessagingStores stores,
    MessagingSources sources,
    MessagingMetricsEventListener metrics,
    IServiceProvider services,
    TimeSpan searchTimeout,
    MessagingForwarding? forwarding = null
)
{
    /// <summary>The most rows one list call returns.</summary>
    internal const int MaxListLimit = 200;

    private const int _DefaultListLimit = 50;
    private const int _MaxScheduledPageSize = 200;
    private const int _MaxGenerationPageSize = 200;

    private readonly MessagingStores _stores = Argument.IsNotNull(stores);
    private readonly MessagingSources _sources = Argument.IsNotNull(sources);
    private readonly MessagingMetricsEventListener _metrics = Argument.IsNotNull(metrics);
    private readonly IServiceProvider _services = Argument.IsNotNull(services);

    /// <summary>
    /// Maps the reads on <paramref name="routes"/>, and the node list and Ping, which are always this node's, on
    /// <paramref name="local"/>. Ping needs operate permission, which <paramref name="endpoints"/> requires.
    /// </summary>
    public void Map(DashboardModuleEndpoints endpoints, IEndpointRouteBuilder routes, IEndpointRouteBuilder local)
    {
        Argument.IsNotNull(endpoints);
        Argument.IsNotNull(routes);
        Argument.IsNotNull(local);

        routes.MapGet("/published", _ListPublishedAsync);
        routes.MapGet("/received", _ListReceivedAsync);
        // A search reads and changes nothing; it is a POST so the text it searches for stays out of URLs and logs.
        routes.MapPost("/published/search", _SearchPublishedAsync).WithMetadata(MessagingReadEndpoint.Instance);
        routes.MapPost("/received/search", _SearchReceivedAsync).WithMetadata(MessagingReadEndpoint.Instance);
        routes.MapGet("/messages/{kind}/{id}", _GetMessageAsync);
        routes.MapGet("/received/generations", _ListGenerationsAsync);
        routes.MapGet("/scheduled", _ListScheduledAsync);
        routes.MapGet("/receipts", _ListReceiptsAsync);
        routes.MapGet("/consumers", _Consumers);
        routes.MapGet("/contracts", _Contracts);
        routes.MapGet("/circuits", _Circuits);
        routes.MapGet("/backlog", _BacklogAsync);
        routes.MapGet("/providers", _Providers);
        local.MapGet("/nodes", _NodesAsync);
        // Ping changes nothing, yet it makes this node call others, so a viewer may not start one.
        endpoints.RequireOperate(local.MapPost("/nodes/ping", _PingAsync));
        routes.MapGet("/metrics/realtime", _Realtime);
        routes.MapGet("/metrics/hourly", _HourlyAsync);
    }

    private Task<Results<JsonHttpResult<MessagingRowList>, ValidationProblem, NotFound>> _ListPublishedAsync(
        [AsParameters] MessagingListRequest request,
        CancellationToken cancellationToken
    )
    {
        return _ReadPublishedAsync(request, search: null, cancellationToken);
    }

    private Task<Results<JsonHttpResult<MessagingRowList>, ValidationProblem, NotFound>> _ListReceivedAsync(
        [AsParameters] MessagingListRequest request,
        CancellationToken cancellationToken
    )
    {
        return _ReadReceivedAsync(request, search: null, cancellationToken);
    }

    private Task<Results<JsonHttpResult<MessagingRowList>, ValidationProblem, NotFound>> _SearchPublishedAsync(
        HttpContext context,
        CancellationToken cancellationToken
    )
    {
        return _SearchAsync(context, _ReadPublishedAsync, cancellationToken);
    }

    private Task<Results<JsonHttpResult<MessagingRowList>, ValidationProblem, NotFound>> _SearchReceivedAsync(
        HttpContext context,
        CancellationToken cancellationToken
    )
    {
        return _SearchAsync(context, _ReadReceivedAsync, cancellationToken);
    }

    private static async Task<Results<JsonHttpResult<MessagingRowList>, ValidationProblem, NotFound>> _SearchAsync(
        HttpContext context,
        Func<
            MessagingListRequest,
            MessagingSearchRequest?,
            CancellationToken,
            Task<Results<JsonHttpResult<MessagingRowList>, ValidationProblem, NotFound>>
        > list,
        CancellationToken cancellationToken
    )
    {
        MessagingSearchRequest? request;

        try
        {
            request = await JsonSerializer
                .DeserializeAsync(
                    context.Request.Body,
                    MessagingDashboardJsonContext.Default.MessagingSearchRequest,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            request = null;
        }

        // The problems name the fields, never the text searched for: a problem is served, and a host may log it.
        if (request is null)
        {
            return DashboardValidation.Problem(
                DashboardValidation.BodyField,
                DashboardValidation.JsonRule,
                "The body must be a search: { content, correlationId, and the list's filters }."
            );
        }

        var validation = await MessagingSearchRequestValidator
            .Instance.ValidateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return validation.IsValid
            ? await list(request.ToListRequest(), request, cancellationToken).ConfigureAwait(false)
            : DashboardValidation.Problem(validation);
    }

    private Task<Results<JsonHttpResult<MessagingRowList>, ValidationProblem, NotFound>> _ReadPublishedAsync(
        MessagingListRequest request,
        MessagingSearchRequest? search,
        CancellationToken cancellationToken
    )
    {
        // Published rows have no consumer and are never held, so those filters do not apply here.
        return _ListAsync(
            MessageType.Publish,
            request.Store,
            request with
            {
                Consumer = null,
                Held = null,
            },
            search,
            cancellationToken
        );
    }

    private Task<Results<JsonHttpResult<MessagingRowList>, ValidationProblem, NotFound>> _ReadReceivedAsync(
        MessagingListRequest request,
        MessagingSearchRequest? search,
        CancellationToken cancellationToken
    )
    {
        // Received rows live in the main store alone.
        return _ListAsync(MessageType.Subscribe, _stores.Primary.Name, request, search, cancellationToken);
    }

    private async Task<Results<JsonHttpResult<MessagingRowList>, ValidationProblem, NotFound>> _ListAsync(
        MessageType type,
        string? storeName,
        MessagingListRequest filter,
        MessagingSearchRequest? search,
        CancellationToken cancellationToken
    )
    {
        var validation = await MessagingListRequestValidator
            .Instance.ValidateAsync(filter, cancellationToken)
            .ConfigureAwait(false);

        if (!validation.IsValid)
        {
            return DashboardValidation.Problem(validation);
        }

        var status = DashboardValidation.ParseEnum<StatusName>(filter.Status);
        var retrying = filter.Retrying == true;
        IReadOnlyList<MessagingStore> targets;

        if (string.IsNullOrEmpty(storeName))
        {
            targets = _stores.All;
        }
        else if (_stores.Find(storeName) is { } named)
        {
            targets = [named];
        }
        else
        {
            return TypedResults.NotFound();
        }

        // The validator accepted the cursor, so it parses.
        _ = MergedListCursor.TryParse(filter.After, out var cursor);

        // A store's list cannot select a failure that has a retry pending, so a retrying list reads every failure and
        // keeps those with one; its count is the store's failures less its terminal ones, as the sidebar counts it.
        var query = new MessageListQuery
        {
            MessageType = type,
            Status = retrying ? StatusName.Failed : status,
            TerminalOnly = filter.Terminal == true,
            ConsumerIdentity = string.IsNullOrEmpty(filter.Consumer) ? null : filter.Consumer,
            Name = string.IsNullOrEmpty(filter.Name) ? null : filter.Name,
            FailureFingerprint = string.IsNullOrEmpty(filter.Fingerprint) ? null : filter.Fingerprint,
            IsHeld = filter.Held,
            Lane = DashboardValidation.ParseEnum<MessageLane>(filter.Lane),
            MessageId = string.IsNullOrEmpty(filter.MessageId) ? null : filter.MessageId,
            AddedFrom = filter.From,
            AddedBefore = filter.Before,
            Content = string.IsNullOrEmpty(search?.Content) ? null : search.Content,
            CorrelationId = string.IsNullOrEmpty(search?.CorrelationId) ? null : search.CorrelationId,
            SortBy = filter.ReadSort(),
            SortDirection = filter.ReadDirection(),
        };

        if (!cursor.Fits(query))
        {
            return DashboardValidation.Problem(
                "after",
                DashboardValidation.InvalidRule,
                "after was read with another sort, direction, filter, or search; read the first page again."
            );
        }

        var take = Math.Clamp(filter.Limit ?? _DefaultListLimit, 1, MaxListLimit);
        var answers = await _stores
            .ReadEachAsync(
                targets,
                async (store, token) =>
                {
                    // A store whose rows an earlier page used up is still counted, so the total covers every store.
                    var page = cursor.IsDone(store.Name)
                        ? null
                        : await store
                            .Monitoring.ListMessagesAsync(query, cursor.PositionOf(store.Name), take, token)
                            .ConfigureAwait(false);
                    var count = await store
                        .Monitoring.CountMessagesAsync(query, MessageCount.DefaultCap, token)
                        .ConfigureAwait(false);

                    if (retrying)
                    {
                        var terminal = await store
                            .Monitoring.CountMessagesAsync(
                                query with
                                {
                                    TerminalOnly = true,
                                },
                                MessageCount.DefaultCap,
                                token
                            )
                            .ConfigureAwait(false);
                        count = new MessageCount(Math.Max(0, count.Count - terminal.Count), count.Capped);
                    }

                    return (page, count);
                },
                // A search reads every row its filters leave, so it waits longer and calls a store out of time incomplete.
                search is null
                    ? _stores.Timeout
                    : searchTimeout,
                cancellationToken
            )
            .ConfigureAwait(false);

        (MessagingStore Store, MessageListPage Page)[] pages =
        [
            .. answers
                .Answered.Where(static answer => answer.Value.page is not null)
                .Select(static answer => (answer.Store, answer.Value.page!)),
        ];
        var (rows, taken) = MergedListCursor.Merge(pages, query, take);
        var next = cursor.Advance(query, targets, pages, taken);
        var incomplete = search is not null && answers.TimedOut.Count > 0;
        var reasons = await _ReadReasonsAsync(type, rows, cancellationToken).ConfigureAwait(false);

        return TypedResults.Json(
            new MessagingRowList(
                [
                    .. rows.Where(row => !retrying || row.View.NextRetryAt is not null)
                        .Select(row =>
                            MessagingRowView.From(
                                new MessagingObjectId(_stores.NodeName, row.Store.Name, row.View.StorageId),
                                row.View
                            ) with
                            {
                                Reason = reasons.GetValueOrDefault((row.Store.Name, row.View.StorageId)),
                            }
                        ),
                ],
                answers.Answered.Sum(static answer => answer.Value.count.Count),
                answers.Answered.Any(static answer => answer.Value.count.Capped),
                next?.ToString(),
                incomplete
                    ? [.. answers.Unavailable.Except(answers.TimedOut, StringComparer.Ordinal)]
                    : answers.Unavailable,
                incomplete
            ),
            MessagingDashboardJsonContext.Default.MessagingRowList
        );
    }

    // A list row carries no failure text, so the page's failed rows are read once more by id, one read per store. The
    // read is an extra: a store that fails or runs out of time here leaves its rows without a reason, never the list
    // without its rows.
    private async Task<IReadOnlyDictionary<(string Store, Guid StorageId), string>> _ReadReasonsAsync(
        MessageType type,
        IReadOnlyList<(MessagingStore Store, MessageView View)> rows,
        CancellationToken cancellationToken
    )
    {
        var failed = rows.Where(static row => row.View.StatusName == StatusName.Failed)
            .GroupBy(static row => row.Store, static row => row.View.StorageId)
            .ToDictionary(static group => group.Key, static group => (IReadOnlyList<Guid>)[.. group]);
        var reasons = new Dictionary<(string Store, Guid StorageId), string>();

        if (failed.Count == 0)
        {
            return reasons;
        }

        var answers = await _stores
            .ReadEachAsync(
                failed.Keys,
                async (store, token) =>
                    type == MessageType.Publish
                        ? await store.Monitoring.GetPublishedMessagesAsync(failed[store], token).ConfigureAwait(false)
                        : await store.Monitoring.GetReceivedMessagesAsync(failed[store], token).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        foreach (var (store, messages) in answers.Answered)
        {
            foreach (var message in messages)
            {
                // The row failed when the list read it; one that has moved on since has no failure to name.
                if (MessagingViews.ReasonOf(StatusName.Failed, message.ExceptionInfo) is { } reason)
                {
                    reasons[(store.Name, message.StorageId)] = reason;
                }
            }
        }

        return reasons;
    }

    private async Task<Results<JsonHttpResult<MessagingMessageDetail>, NotFound>> _GetMessageAsync(
        string kind,
        string id,
        CancellationToken cancellationToken
    )
    {
        if (
            !MessagingViews.TryParseKind(kind, out var type)
            || !MessagingObjectId.TryParse(id, out var objectId)
            || await _sources.FindAsync(type, objectId, cancellationToken).ConfigureAwait(false) is not { } found
        )
        {
            return TypedResults.NotFound();
        }

        var view = await MessagingViews
            .ReadViewAsync(
                found.Store,
                type,
                objectId.StorageId,
                MessagingViews.MessageIdOf(found.Message),
                cancellationToken
            )
            .ConfigureAwait(false);

        return TypedResults.Json(
            new MessagingMessageDetail(
                view is null
                    ? MessagingRowView.From(found.Id, type, found.Message)
                    : MessagingRowView.From(found.Id, view) with
                    {
                        Reason = MessagingViews.ReasonOf(view.StatusName, found.Message.ExceptionInfo),
                    },
                new Dictionary<string, string?>(found.Message.Origin.Headers, StringComparer.Ordinal),
                found.Message.Content,
                found.Message.ExceptionInfo
            ),
            MessagingDashboardJsonContext.Default.MessagingMessageDetail
        );
    }

    private async Task<Results<JsonHttpResult<MessagingGenerationList>, ValidationProblem>> _ListGenerationsAsync(
        HttpContext context,
        string? messageId,
        string? consumer,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(messageId))
        {
            return DashboardValidation.Problem("messageId", DashboardValidation.RequiredRule, "messageId is required.");
        }

        var store = _stores.Primary;
        var consumerFilter = string.IsNullOrEmpty(consumer) ? null : consumer;
        var page = await store
            .Monitoring.ListMessagesAsync(
                new MessageListQuery
                {
                    MessageType = MessageType.Subscribe,
                    MessageId = messageId,
                    ConsumerIdentity = consumerFilter,
                },
                after: null,
                MessageListPage.MaxPageSize,
                cancellationToken
            )
            .ConfigureAwait(false);
        var generations = await _ReadGenerationsAsync(
                store,
                context,
                messageId,
                consumerFilter,
                storageId => new MessagingObjectId(_stores.NodeName, store.Name, storageId).ToString(),
                cancellationToken
            )
            .ConfigureAwait(false);

        return TypedResults.Json(
            new MessagingGenerationList([
                .. page
                    .Items
                    // SQL Server's '=' ignores trailing spaces, so the store's match is confirmed byte for byte.
                    .Where(row =>
                        string.Equals(row.MessageId, messageId, StringComparison.Ordinal)
                        && (
                            consumerFilter is null
                            || string.Equals(row.ConsumerIdentity, consumerFilter, StringComparison.Ordinal)
                        )
                    )
                    .OrderBy(static row => row.Added)
                    .Select(row =>
                        MessagingRowView.From(
                            new MessagingObjectId(_stores.NodeName, store.Name, row.StorageId),
                            row
                        ) with
                        {
                            Generation = generations.GetValueOrDefault(row.StorageId),
                        }
                    ),
            ]),
            MessagingDashboardJsonContext.Default.MessagingGenerationList
        );
    }

    // Each generation's inbox fields, by the row they belong to. A store without inbox operations, or a row admitted
    // without a generation, serves its row with none, never an error.
    private static async Task<IReadOnlyDictionary<Guid, MessagingGenerationView>> _ReadGenerationsAsync(
        MessagingStore store,
        HttpContext context,
        string messageId,
        string? consumer,
        Func<Guid, string> idOf,
        CancellationToken cancellationToken
    )
    {
        IndexPage<InboxGenerationView> generations;

        try
        {
            generations = await store
                .Storage.GetInboxOperationsApi()
                .QueryAsync(
                    new InboxGenerationQuery
                    {
                        MessageId = messageId,
                        ConsumerIdentity = consumer,
                        // The most a generation read returns; a row past it is served without its generation.
                        PageSize = _MaxGenerationPageSize,
                    },
                    new OperatorAuthorizationContext(Reader(context.User)),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            return new Dictionary<Guid, MessagingGenerationView>();
        }

        // The rows the store's '=' matched with trailing spaces belong to another message.
        var matched = generations
            .Items.Where(generation => string.Equals(generation.MessageId, messageId, StringComparison.Ordinal))
            .ToArray();
        var rowsByIncarnation = matched.ToDictionary(static generation => generation.IncarnationId);

        return matched.ToDictionary(
            static generation => generation.StorageId,
            generation =>
                MessagingGenerationView.From(
                    generation,
                    generation.ReplayParentIncarnationId is { } parent
                    && rowsByIncarnation.TryGetValue(parent, out var parentRow)
                        ? idOf(parentRow.StorageId)
                        : null
                )
        );
    }

    private async Task<Results<JsonHttpResult<MessagingScheduledList>, NotFound>> _ListScheduledAsync(
        HttpContext context,
        string? store,
        string? name,
        int? page,
        int? perPage,
        CancellationToken cancellationToken
    )
    {
        var target = string.IsNullOrEmpty(store) ? _stores.Primary : _stores.Find(store);

        if (target is null)
        {
            return TypedResults.NotFound();
        }

        IScheduledDeliveryOperationsApi operations;

        try
        {
            operations = target.Storage.GetScheduledDeliveryOperationsApi();
        }
        catch (NotSupportedException exception)
        {
            return TypedResults.Json(
                _Unsupported(target, exception),
                MessagingDashboardJsonContext.Default.MessagingScheduledList
            );
        }

        try
        {
            // A read: it needs an authenticated principal, not a named operator.
            var result = await operations
                .QueryAsync(
                    new ScheduledDeliveryQuery
                    {
                        MessageName = string.IsNullOrEmpty(name) ? null : name,
                        CurrentPage = Math.Max((page ?? 1) - 1, 0),
                        PageSize = Math.Clamp(perPage ?? 20, 1, _MaxScheduledPageSize),
                    },
                    new OperatorAuthorizationContext(Reader(context.User)),
                    cancellationToken
                )
                .ConfigureAwait(false);

            return TypedResults.Json(
                new MessagingScheduledList(target.Name, Supported: true, result.Items, result.TotalItems, Reason: null),
                MessagingDashboardJsonContext.Default.MessagingScheduledList
            );
        }
        catch (NotSupportedException exception)
        {
            return TypedResults.Json(
                _Unsupported(target, exception),
                MessagingDashboardJsonContext.Default.MessagingScheduledList
            );
        }
    }

    private async Task<
        Results<JsonHttpResult<MessagingReceiptList>, ValidationProblem, ProblemHttpResult>
    > _ListReceiptsAsync(
        HttpContext context,
        [AsParameters] MessagingReceiptListRequest request,
        CancellationToken cancellationToken
    )
    {
        var validation = await MessagingReceiptListRequestValidator
            .Instance.ValidateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!validation.IsValid)
        {
            return DashboardValidation.Problem(validation);
        }

        // The validator accepted the cursor, so it parses.
        _ = ReceiptListCursor.TryParse(request.After, out var position);

        // The ledger lives in the main store alone: an additional outbox's receipts name it in OutboxName.
        try
        {
            var page = await _stores
                .Primary.Storage.GetInboxOperationsApi()
                .QueryReceiptsAsync(
                    new MessagingOperationReceiptQuery
                    {
                        Actor = string.IsNullOrEmpty(request.Actor) ? null : request.Actor,
                        TargetKind = DashboardValidation.ParseEnum<MessagingOperationTargetKind>(request.TargetKind),
                        OperationType = DashboardValidation.ParseEnum<MessagingOperationType>(request.OperationType),
                        From = request.From,
                        Before = request.Before,
                        After = position,
                        Limit = Math.Clamp(
                            request.Limit ?? _DefaultListLimit,
                            1,
                            MessagingOperationReceiptQuery.MaxLimit
                        ),
                    },
                    new OperatorAuthorizationContext(Reader(context.User)),
                    cancellationToken
                )
                .ConfigureAwait(false);

            return TypedResults.Json(
                new MessagingReceiptList(page.Items, page.Next is { } next ? ReceiptListCursor.Format(next) : null),
                MessagingDashboardJsonContext.Default.MessagingReceiptList
            );
        }
        catch (NotSupportedException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status501NotImplemented);
        }
    }

    // The shell authorized the viewer before this endpoint ran. Without dashboard authentication its user is not
    // signed in, yet a read needs only an authenticated principal, never a named operator, so the read runs as the
    // dashboard's viewer.
    internal static ClaimsPrincipal Reader(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user : _DashboardViewer;

    private static readonly ClaimsPrincipal _DashboardViewer = new(
        new ClaimsIdentity(authenticationType: "HeadlessDashboard")
    );

    private static MessagingScheduledList _Unsupported(MessagingStore store, NotSupportedException exception) =>
        new(store.Name, Supported: false, [], TotalCount: 0, exception.Message);

    private FileContentHttpResult _Consumers() => _ManifestPart("consumers");

    private FileContentHttpResult _Contracts() => _ManifestPart("messages");

    private JsonHttpResult<MessagingCircuitList> _Circuits()
    {
        var monitor = _services.GetService<ICircuitBreakerMonitor>();
        var items = new List<MessagingCircuitView>();

        foreach (var consumer in (IEnumerable<string>?)monitor?.KnownConsumers.Order(StringComparer.Ordinal) ?? [])
        {
            if (monitor!.GetSnapshot(consumer) is { } snapshot)
            {
                var (identity, lane) = MessagingViews.ReadCircuitKey(consumer);

                items.Add(
                    new MessagingCircuitView(
                        MessagingViews.CircuitId(_stores.NodeName, consumer),
                        identity,
                        lane,
                        snapshot.State,
                        snapshot.ConsecutiveFailures,
                        snapshot.FailureThreshold,
                        snapshot.OpenedAt,
                        snapshot.EstimatedRemainingOpenDuration
                    )
                );
            }
        }

        return TypedResults.Json(
            new MessagingCircuitList(items),
            MessagingDashboardJsonContext.Default.MessagingCircuitList
        );
    }

    // Every store's backlog, read at once. A store that fails or does not answer in time is named, so a missing store
    // never reads as an empty backlog.
    private async Task<JsonHttpResult<MessagingBacklog>> _BacklogAsync(CancellationToken cancellationToken)
    {
        var asOf = (_services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        var answers = await _stores
            .ReadEachAsync(
                _stores.All,
                static async (store, token) => await store.Monitoring.GetBacklogAsync(token).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        return TypedResults.Json(
            new MessagingBacklog(
                asOf,
                [
                    .. answers.Answered.Select(static answer => new MessagingStoreBacklog(
                        answer.Store.Name,
                        answer.Value.PublishedPending,
                        answer.Value.OldestPublishedPendingAge?.TotalSeconds,
                        answer.Store.HoldsReceived ? answer.Value.ReceivedPending : null,
                        answer.Store.HoldsReceived ? answer.Value.OldestReceivedPendingAge?.TotalSeconds : null
                    )),
                ],
                answers.Unavailable
            ),
            MessagingDashboardJsonContext.Default.MessagingBacklog
        );
    }

    // This node, unless it is a dashboard-only host that reads no store, and every node discovery lists, each with what
    // the last forwarded call to it found.
    private async Task<JsonHttpResult<MessagingNodeList>> _NodesAsync(CancellationToken cancellationToken)
    {
        var items = new List<MessagingNodeView>();

        if (forwarding?.IsStandalone != true)
        {
            items.Add(
                new MessagingNodeView(
                    _stores.NodeName,
                    IsLocal: true,
                    [.. _stores.All.Select(static store => store.Name)]
                )
            );
        }

        string? discovery = null;

        if (forwarding?.IsEnabled == true)
        {
            var peers = await forwarding.Directory.GetPeersAsync(cancellationToken).ConfigureAwait(false);

            items.AddRange(
                peers.Select(peer => new MessagingNodeView(
                    peer.Name,
                    IsLocal: false,
                    Stores: [],
                    State: forwarding.Directory.StatusOf(peer.Name)?.State.ToWire(),
                    Instances: peer.Instances
                ))
            );

            discovery = forwarding.Directory.DiscoveryState;
        }

        return TypedResults.Json(
            new MessagingNodeList(items) { Discovery = discovery },
            MessagingDashboardJsonContext.Default.MessagingNodeList
        );
    }

    // Every store's published rows, and the main store's received rows, per hour over the last 24, read at once. A store
    // that fails or does not answer in time has null series and is named, so a missing store never reads as zero.
    private async Task<JsonHttpResult<MessagingHourlyHistory>> _HourlyAsync(CancellationToken cancellationToken)
    {
        var asOf = (_services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        var current = asOf.TruncateToHours();
        DateTimeOffset[] hours =
        [
            .. Enumerable
                .Range(0, MessagingHourlyHistory.Hours)
                .Select(i => current.AddHours(i - (MessagingHourlyHistory.Hours - 1))),
        ];

        var answers = await _stores
            .ReadEachAsync(
                _stores.All,
                async (store, token) =>
                {
                    var published = await _ReadHourlyAsync(store.Monitoring, MessageType.Publish, hours, token)
                        .ConfigureAwait(false);
                    var received = store.HoldsReceived
                        ? await _ReadHourlyAsync(store.Monitoring, MessageType.Subscribe, hours, token)
                            .ConfigureAwait(false)
                        : null;

                    return (published, received);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var byStore = answers.Answered.ToDictionary(static answer => answer.Store.Name, StringComparer.Ordinal);

        return TypedResults.Json(
            new MessagingHourlyHistory(
                asOf,
                hours,
                [
                    .. _stores.All.Select(store =>
                        byStore.TryGetValue(store.Name, out var answer)
                            ? new MessagingHourlyStore(store.Name, answer.Value.published, answer.Value.received)
                            : new MessagingHourlyStore(store.Name, Published: null, Received: null)
                    ),
                ],
                answers.Unavailable
            ),
            MessagingDashboardJsonContext.Default.MessagingHourlyHistory
        );
    }

    private static async Task<MessagingHourlySeries> _ReadHourlyAsync(
        IMonitoringApi monitoring,
        MessageType type,
        IReadOnlyList<DateTimeOffset> hours,
        CancellationToken cancellationToken
    )
    {
        var succeeded = await monitoring.GetHourlySucceededJobsAsync(type, cancellationToken).ConfigureAwait(false);
        var failed = await monitoring.GetHourlyFailedJobsAsync(type, cancellationToken).ConfigureAwait(false);

        // The store keys every hour it counted, an empty one as zero. An hour it has no key for is one it did not count,
        // as when the hour turned between this node's clock read and the store's, so it is null, never zero.
        return new MessagingHourlySeries(
            [.. hours.Select(hour => succeeded.TryGetValue(hour, out var count) ? count : (int?)null)],
            [.. hours.Select(hour => failed.TryGetValue(hour, out var count) ? count : (int?)null)]
        );
    }

    private async Task<
        Results<JsonHttpResult<MessagingPingResult>, ValidationProblem, ProblemHttpResult, NotFound>
    > _PingAsync(HttpContext context, CancellationToken cancellationToken)
    {
        // A call another node forwarded never makes this node call further nodes.
        if (MessagingForwarding.IsForwarded(context.User))
        {
            return TypedResults.NotFound();
        }

        MessagingPingRequest? request;

        try
        {
            request = context.Request.ContentLength is 0
                ? new MessagingPingRequest(Node: null)
                : await JsonSerializer
                    .DeserializeAsync(
                        context.Request.Body,
                        MessagingDashboardJsonContext.Default.MessagingPingRequest,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null)
        {
            return DashboardValidation.Problem(
                DashboardValidation.BodyField,
                DashboardValidation.JsonRule,
                "The body must be { node } to ping one node, or {} to ping every node discovery lists."
            );
        }

        var directory = forwarding?.IsEnabled == true ? forwarding.Directory : null;
        IReadOnlyList<MessagingPeer> peers;

        if (string.IsNullOrEmpty(request.Node))
        {
            peers = directory is null ? [] : await directory.GetPeersAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (
            string.Equals(request.Node, _stores.NodeName, StringComparison.Ordinal)
            || directory?.IsLocal(request.Node) == true
        )
        {
            return DashboardValidation.Problem(
                "node",
                DashboardValidation.InvalidRule,
                "node names this node, which is serving the page; Ping reaches other nodes."
            );
        }
        // Only discovery entries are callable, so a caller cannot point this node at an address of its choosing.
        else if (
            directory is not null
            && await directory.FindAsync(request.Node, cancellationToken).ConfigureAwait(false) is { } peer
        )
        {
            peers = [peer];
        }
        else
        {
            return TypedResults.Problem(
                detail: $"Discovery lists no Messaging node named '{request.Node}'.",
                statusCode: StatusCodes.Status404NotFound
            );
        }

        var answers =
            peers.Count == 0
                ? []
                : await NodePing
                    .PingAsync(
                        forwarding!.Client,
                        peers,
                        _services.GetService<TimeProvider>() ?? TimeProvider.System,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

        return TypedResults.Json(
            new MessagingPingResult(answers) { Discovery = directory?.DiscoveryState },
            MessagingDashboardJsonContext.Default.MessagingPingResult
        );
    }

    private JsonHttpResult<MessagingRealtimeMetrics> _Realtime()
    {
        var (seconds, published, consumed, elapsed) = _metrics.GetSeries();

        return TypedResults.Json(
            new MessagingRealtimeMetrics(seconds, published, consumed, elapsed),
            MessagingDashboardJsonContext.Default.MessagingRealtimeMetrics
        );
    }

    // The host manifest's messaging section is the authority on what this host registered, so the consumer, contract,
    // and provider views read it rather than a second projection of the registry.
    private FileContentHttpResult _ManifestPart(string property)
    {
        using var manifest = _ReadManifest();
        var output = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("items");

            if (manifest?.RootElement.TryGetProperty(property, out var part) == true)
            {
                part.WriteTo(writer);
            }
            else
            {
                writer.WriteStartArray();
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return TypedResults.Bytes(output.WrittenMemory.ToArray(), "application/json");
    }

    // What this host publishes through: the broker of each lane and the storage provider of each outbox, by name only,
    // as the manifest writes them. A host without a transport or storage has an empty part, never a missing one.
    private FileContentHttpResult _Providers()
    {
        using var manifest = _ReadManifest();
        var output = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("transports");

            if (manifest?.RootElement.TryGetProperty("transports", out var transports) == true)
            {
                transports.WriteTo(writer);
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }

            writer.WritePropertyName("outboxes");

            if (manifest?.RootElement.TryGetProperty("outboxes", out var outboxes) == true)
            {
                outboxes.WriteTo(writer);
            }
            else
            {
                writer.WriteStartArray();
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return TypedResults.Bytes(output.WrittenMemory.ToArray(), "application/json");
    }

    private JsonDocument? _ReadManifest()
    {
        var section = _services
            .GetServices<IHostManifestSection>()
            .FirstOrDefault(static section => string.Equals(section.Name, "messaging", StringComparison.Ordinal));

        if (section is null)
        {
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            section.Write(writer);
        }

        return JsonDocument.Parse(buffer.WrittenMemory);
    }
}
