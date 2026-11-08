// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Security.Claims;
using Headless.Checks;
using Headless.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The Messaging module's own API at <c>{base}/api/messaging</c>: published and received lists over every store,
/// message detail, the generations of a received message, scheduled deliveries, consumers and contracts from the host
/// manifest, circuit breakers, nodes, and this node's real-time throughput. Every call is a read, so a viewer without
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
    MessagingForwarding? forwarding = null
)
{
    /// <summary>The most rows one list call returns.</summary>
    internal const int MaxListLimit = 200;

    private const int _DefaultListLimit = 50;
    private const int _MaxScheduledPageSize = 200;

    private readonly MessagingStores _stores = Argument.IsNotNull(stores);
    private readonly MessagingSources _sources = Argument.IsNotNull(sources);
    private readonly MessagingMetricsEventListener _metrics = Argument.IsNotNull(metrics);
    private readonly IServiceProvider _services = Argument.IsNotNull(services);

    /// <summary>Maps the reads on <paramref name="routes"/>, and the node list, which is always this node's, on <paramref name="local"/>.</summary>
    public void Map(IEndpointRouteBuilder routes, IEndpointRouteBuilder local)
    {
        Argument.IsNotNull(routes);
        Argument.IsNotNull(local);

        routes.MapGet("/published", _ListPublishedAsync);
        routes.MapGet("/received", _ListReceivedAsync);
        routes.MapGet("/messages/{kind}/{id}", _GetMessageAsync);
        routes.MapGet("/received/generations", _ListGenerationsAsync);
        routes.MapGet("/scheduled", _ListScheduledAsync);
        routes.MapGet("/consumers", _Consumers);
        routes.MapGet("/contracts", _Contracts);
        routes.MapGet("/circuits", _Circuits);
        local.MapGet("/nodes", _NodesAsync);
        routes.MapGet("/metrics/realtime", _Realtime);
    }

    private Task<Results<JsonHttpResult<MessagingRowList>, BadRequest<string>, NotFound>> _ListPublishedAsync(
        string? store,
        string? status,
        bool? terminal,
        bool? retrying,
        string? name,
        string? fingerprint,
        int? limit,
        string? after,
        CancellationToken cancellationToken
    )
    {
        return _ListAsync(
            MessageType.Publish,
            store,
            new ListFilter(status, terminal, retrying, Consumer: null, name, fingerprint, Held: null),
            limit,
            after,
            cancellationToken
        );
    }

    private Task<Results<JsonHttpResult<MessagingRowList>, BadRequest<string>, NotFound>> _ListReceivedAsync(
        string? status,
        bool? terminal,
        bool? retrying,
        string? consumer,
        string? name,
        string? fingerprint,
        bool? held,
        int? limit,
        string? after,
        CancellationToken cancellationToken
    )
    {
        // Received rows live in the main store alone.
        return _ListAsync(
            MessageType.Subscribe,
            _stores.Primary.Name,
            new ListFilter(status, terminal, retrying, consumer, name, fingerprint, held),
            limit,
            after,
            cancellationToken
        );
    }

    private async Task<Results<JsonHttpResult<MessagingRowList>, BadRequest<string>, NotFound>> _ListAsync(
        MessageType type,
        string? storeName,
        ListFilter filter,
        int? limit,
        string? after,
        CancellationToken cancellationToken
    )
    {
        StatusName? status = null;
        var retrying = filter.Retrying == true;

        if (retrying && (filter.Terminal == true || !string.IsNullOrEmpty(filter.Status)))
        {
            return TypedResults.BadRequest(
                "retrying selects failed rows with a retry pending; it takes no status or terminal."
            );
        }

        if (!string.IsNullOrEmpty(filter.Status))
        {
            if (!Enum.TryParse<StatusName>(filter.Status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return TypedResults.BadRequest($"'{filter.Status}' is not a message status.");
            }

            status = parsed;
        }

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

        if (!MergedListCursor.TryParse(after, out var cursor))
        {
            return TypedResults.BadRequest("after is not a cursor this list returned.");
        }

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
        };

        var take = Math.Clamp(limit ?? _DefaultListLimit, 1, MaxListLimit);
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
                cancellationToken
            )
            .ConfigureAwait(false);

        (MessagingStore Store, MessageListPage Page)[] pages =
        [
            .. answers
                .Answered.Where(static answer => answer.Value.page is not null)
                .Select(static answer => (answer.Store, answer.Value.page!)),
        ];
        var (rows, taken) = MergedListCursor.Merge(pages, take);
        var next = cursor.Advance(targets, pages, taken);

        return TypedResults.Json(
            new MessagingRowList(
                [
                    .. rows.Where(row => !retrying || row.View.NextRetryAt is not null)
                        .Select(row =>
                            MessagingRowView.From(
                                new MessagingObjectId(_stores.NodeName, row.Store.Name, row.View.StorageId),
                                row.View
                            )
                        ),
                ],
                answers.Answered.Sum(static answer => answer.Value.count.Count),
                answers.Answered.Any(static answer => answer.Value.count.Capped),
                next?.ToString(),
                answers.Unavailable
            ),
            MessagingDashboardJsonContext.Default.MessagingRowList
        );
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
                    : MessagingRowView.From(found.Id, view),
                new Dictionary<string, string?>(found.Message.Origin.Headers, StringComparer.Ordinal),
                found.Message.Content,
                found.Message.ExceptionInfo
            ),
            MessagingDashboardJsonContext.Default.MessagingMessageDetail
        );
    }

    private async Task<Results<JsonHttpResult<MessagingGenerationList>, BadRequest<string>>> _ListGenerationsAsync(
        string? messageId,
        string? consumer,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(messageId))
        {
            return TypedResults.BadRequest("messageId is required.");
        }

        var store = _stores.Primary;
        var page = await store
            .Monitoring.ListMessagesAsync(
                new MessageListQuery
                {
                    MessageType = MessageType.Subscribe,
                    MessageId = messageId,
                    ConsumerIdentity = string.IsNullOrEmpty(consumer) ? null : consumer,
                },
                after: null,
                MessageListPage.MaxPageSize,
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
                            string.IsNullOrEmpty(consumer)
                            || string.Equals(row.ConsumerIdentity, consumer, StringComparison.Ordinal)
                        )
                    )
                    .OrderBy(static row => row.Added)
                    .Select(row =>
                        MessagingRowView.From(new MessagingObjectId(_stores.NodeName, store.Name, row.StorageId), row)
                    ),
            ]),
            MessagingDashboardJsonContext.Default.MessagingGenerationList
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

    private IResult _Consumers() => _ManifestPart("consumers");

    private IResult _Contracts() => _ManifestPart("messages");

    private JsonHttpResult<MessagingCircuitList> _Circuits()
    {
        var monitor = _services.GetService<ICircuitBreakerMonitor>();
        var items = new List<MessagingCircuitView>();

        foreach (var consumer in (IEnumerable<string>?)monitor?.KnownConsumers.Order(StringComparer.Ordinal) ?? [])
        {
            if (monitor!.GetSnapshot(consumer) is { } snapshot)
            {
                items.Add(
                    new MessagingCircuitView(
                        MessagingViews.CircuitId(_stores.NodeName, consumer),
                        consumer,
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
                    State: forwarding.Directory.StatusOf(peer.Name)?.State.ToWire()
                ))
            );

            discovery = forwarding.Directory.DiscoveryState;
        }

        return TypedResults.Json(
            new MessagingNodeList(items) { Discovery = discovery },
            MessagingDashboardJsonContext.Default.MessagingNodeList
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

    // The host manifest's messaging section is the authority on what this host registered, so the consumer and
    // contract views read it rather than a second projection of the registry.
    private IResult _ManifestPart(string property)
    {
        var section = _services
            .GetServices<IHostManifestSection>()
            .FirstOrDefault(static section => string.Equals(section.Name, "messaging", StringComparison.Ordinal));

        if (section is null)
        {
            return TypedResults.Content("{\"items\":[]}", "application/json");
        }

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            section.Write(writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        var output = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("items");

            if (document.RootElement.TryGetProperty(property, out var part))
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

    private sealed record ListFilter(
        string? Status,
        bool? Terminal,
        bool? Retrying,
        string? Consumer,
        string? Name,
        string? Fingerprint,
        bool? Held
    );
}
