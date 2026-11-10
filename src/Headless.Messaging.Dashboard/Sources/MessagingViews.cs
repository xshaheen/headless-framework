// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The object kinds the Messaging module shows, their ids, and how stored rows read as the shell's objects and changes.
/// </summary>
/// <remarks>
/// An object id is <c>{node}~{store}~{storage id}</c>: the node that serves the row, written by its configured
/// <see cref="MessagingDashboardOptions.NodeName"/>, the store that holds it (<c>primary</c> for the main store, else
/// the outbox's name), and the row's storage id. The stores are shared by every node, so a change has no
/// <see cref="ObjectRef.Node"/> and is sent on the topics <c>messaging.published</c> and <c>messaging.received</c>.
/// </remarks>
internal static class MessagingViews
{
    /// <summary>A published (outbox) row; its stream topic is <c>messaging.published</c>.</summary>
    public const string PublishedKind = "published";

    /// <summary>A received (inbox) row; its stream topic is <c>messaging.received</c>.</summary>
    public const string ReceivedKind = "received";

    /// <summary>A consumer identity, from the host manifest.</summary>
    public const string ConsumerKind = "consumer";

    /// <summary>
    /// One node's circuit breaker for one consumer, with the id <c>{node}~{circuit key}</c>. A circuit lives in a node's
    /// memory, so its id names the node and its commands act on that node alone.
    /// </summary>
    public const string CircuitKind = "circuit";

    /// <summary>A node serving the module, with its node name as the id; its retry back-pressure lives in its memory.</summary>
    public const string NodeKind = "node";

    public static string CircuitId(string node, string circuitKey) =>
        node + MessagingObjectId.Separator.ToString() + circuitKey;

    /// <summary>
    /// Splits a circuit id at its first separator: a node name never holds one, and the circuit key may.
    /// </summary>
    public static bool TryParseCircuitId(string? id, out string node, out string circuitKey)
    {
        var separator = id?.IndexOf(MessagingObjectId.Separator, StringComparison.Ordinal) ?? -1;

        if (separator <= 0 || separator == id!.Length - 1)
        {
            node = string.Empty;
            circuitKey = string.Empty;

            return false;
        }

        node = id[..separator];
        circuitKey = id[(separator + 1)..];

        return true;
    }

    public static string KindOf(MessageType type) => type == MessageType.Publish ? PublishedKind : ReceivedKind;

    public static string KindOf(Persistence.MessageChangeKind kind) =>
        kind == Persistence.MessageChangeKind.Published ? PublishedKind : ReceivedKind;

    public static bool TryParseKind(string? kind, out MessageType type)
    {
        type = string.Equals(kind, ReceivedKind, StringComparison.Ordinal)
            ? MessageType.Subscribe
            : MessageType.Publish;

        return string.Equals(kind, PublishedKind, StringComparison.Ordinal)
            || string.Equals(kind, ReceivedKind, StringComparison.Ordinal);
    }

    public static ObjectRef Ref(string kind, string id) =>
        new()
        {
            Module = MessagingDashboardOptions.ModuleId,
            Kind = kind,
            Id = id,
        };

    public static ObjectRef Ref(MessageType type, MessagingObjectId id) => Ref(KindOf(type), id.ToString());

    /// <summary>The version a change of a row carries: its change marker in UTC ticks, fixed-width for the shell.</summary>
    public static string VersionOf(DateTimeOffset changedAt) =>
        changedAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture);

    public static DashboardObject ToObject(MessageType type, MessagingObjectId id, MediumMessage message) =>
        new()
        {
            Target = Ref(type, id),
            Title = MessageNameOf(message) ?? "(unnamed)",
            Status = message.StatusName?.ToString(),
            TraceId = _TraceIdOf(message),
        };

    public static DashboardChange Change(
        MessageType type,
        MessagingObjectId id,
        MessagingRowView row,
        string version,
        bool affectsAttention
    ) =>
        new()
        {
            Target = Ref(type, id),
            Version = version,
            Projection = JsonSerializer.SerializeToElement(row, MessagingDashboardJsonContext.Default.MessagingRowView),
            AffectsAttention = affectsAttention,
        };

    /// <summary>A change that makes views refetch a row that is gone, so they show it no longer exists.</summary>
    public static DashboardChange Removed(MessageType type, MessagingObjectId id, string version) =>
        new()
        {
            Target = Ref(type, id),
            Version = version,
            Projection = null,
            AffectsAttention = true,
        };

    /// <summary>
    /// Whether a row in <paramref name="status"/> can enter or leave a Needs-you group: a failure, or a success that
    /// can follow one.
    /// </summary>
    public static bool AffectsAttention(StatusName? status) => status is StatusName.Failed or StatusName.Succeeded;

    internal static string? MessageNameOf(MediumMessage message) =>
        message.Origin.Headers.TryGetValue(Headers.MessageName, out var name) ? name : null;

    internal static string? MessageIdOf(MediumMessage message) =>
        message.Origin.Headers.TryGetValue(Headers.MessageId, out var id) ? id : null;

    /// <summary>
    /// Reads the list row of one stored row, which carries what a by-id read lacks, such as a received row's consumer,
    /// hold, and change marker; <see langword="null"/> when the row has no message id or is gone.
    /// </summary>
    /// <remarks>
    /// A list filters by message id, not storage id, so the rows of every consumer and generation of the message come
    /// back and the one asked for is picked by its storage id.
    /// </remarks>
    public static async Task<MessageView?> ReadViewAsync(
        MessagingStore store,
        MessageType type,
        Guid storageId,
        string? messageId,
        CancellationToken cancellationToken
    )
    {
        if (messageId is null)
        {
            return null;
        }

        var page = await store
            .Monitoring.ListMessagesAsync(
                new MessageListQuery { MessageType = type, MessageId = messageId },
                after: null,
                _ViewPageSize,
                cancellationToken
            )
            .ConfigureAwait(false);

        return page.Items.FirstOrDefault(row => row.StorageId == storageId);
    }

    // A message id is shared by the rows of every consumer and generation; a page this size holds them all in practice.
    private const int _ViewPageSize = 100;

    private static string? _TraceIdOf(MediumMessage message)
    {
        // A W3C traceparent is version-traceid-parentid-flags; the trace id is the second field.
        if (!message.Origin.Headers.TryGetValue(Headers.TraceParent, out var traceParent) || traceParent is null)
        {
            return null;
        }

        var parts = traceParent.Split('-');

        return parts.Length == 4 && parts[1].Length == 32 ? parts[1] : null;
    }
}

/// <summary>
/// The id of one Messaging object: <c>{node}~{store}~{storage id}</c>, with the main store written as
/// <c>primary</c>.
/// </summary>
/// <param name="Node">The node that serves the row.</param>
/// <param name="Store">The store that holds the row.</param>
/// <param name="StorageId">The row's storage id.</param>
internal readonly record struct MessagingObjectId(string Node, string Store, Guid StorageId)
{
    public const char Separator = '~';

    public override string ToString() => $"{Node}{Separator}{Store}{Separator}{StorageId:D}";

    public static bool TryParse(string? value, out MessagingObjectId id)
    {
        id = default;
        var parts = value?.Split(Separator);

        if (
            parts is not { Length: 3 }
            || parts[0].Length == 0
            || parts[1].Length == 0
            || !Guid.TryParse(parts[2], out var storageId)
        )
        {
            return false;
        }

        id = new MessagingObjectId(parts[0], parts[1], storageId);

        return true;
    }
}

/// <summary>The slim projection of one row that a list row patches itself with, and the row of every list.</summary>
internal sealed record MessagingRowView
{
    public required string Id { get; init; }

    public required string Store { get; init; }

    public required Guid StorageId { get; init; }

    public string? MessageId { get; init; }

    public string? Name { get; init; }

    public MessageLane Lane { get; init; }

    public StatusName? Status { get; init; }

    public string? Consumer { get; init; }

    public int Retries { get; init; }

    public DateTimeOffset Added { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? NextRetryAt { get; init; }

    public DateTimeOffset? ChangedAt { get; init; }

    public string? FailureFingerprint { get; init; }

    public string? TenantId { get; init; }

    /// <summary>Whether a received row is held; <see langword="null"/> when the read did not say.</summary>
    public bool? IsHeld { get; init; }

    /// <summary>Whether the row failed for good: <see cref="StatusName.Failed"/> with no retry pending.</summary>
    public bool Terminal => Status == StatusName.Failed && NextRetryAt is null;

    public static MessagingRowView From(MessagingObjectId id, MessageView view) =>
        new()
        {
            Id = id.ToString(),
            Store = id.Store,
            StorageId = view.StorageId,
            MessageId = view.MessageId,
            Name = view.Name,
            Lane = view.Lane,
            Status = view.StatusName,
            Consumer = view.ConsumerIdentity,
            Retries = view.Retries,
            Added = view.Added,
            ExpiresAt = view.ExpiresAt,
            NextRetryAt = view.NextRetryAt,
            ChangedAt = view.ChangedAt,
            FailureFingerprint = view.FailureFingerprint,
            TenantId = view.TenantId,
            IsHeld = view.IsHeld,
        };

    public static MessagingRowView From(
        MessagingObjectId id,
        MessageType type,
        MediumMessage message,
        StatusName? status = null,
        DateTimeOffset? changedAt = null
    ) =>
        new()
        {
            Id = id.ToString(),
            Store = id.Store,
            StorageId = message.StorageId,
            MessageId = MessagingViews.MessageIdOf(message),
            Name = MessagingViews.MessageNameOf(message),
            Lane = message.Lane,
            Status = message.StatusName ?? status,
            Consumer =
                type == MessageType.Subscribe
                    ? message.InboxKey?.ConsumerIdentity ?? message.Origin.GetConsumerIdentity()
                    : null,
            Retries = message.Retries,
            Added = message.Added,
            ExpiresAt = message.ExpiresAt,
            NextRetryAt = message.NextRetryAt,
            ChangedAt = changedAt,
            FailureFingerprint = message.FailureFingerprint,
            TenantId = message.InboxKey?.TenantId,
        };
}

/// <summary>One row with everything its detail page shows: headers, content, and the failure.</summary>
internal sealed record MessagingMessageDetail(
    MessagingRowView Row,
    IReadOnlyDictionary<string, string?> Headers,
    string Content,
    string? ExceptionInfo
);

/// <summary>
/// One page of a list over one table of one or more stores: the rows, the stores' summed capped count, and the stores
/// that did not answer.
/// </summary>
internal sealed record MessagingRowList(
    IReadOnlyList<MessagingRowView> Items,
    int Count,
    bool CountCapped,
    string? NextCursor,
    IReadOnlyList<string> Unavailable
);

/// <summary>The generations of one received message: its rows across consumers and re-executions.</summary>
internal sealed record MessagingGenerationList(IReadOnlyList<MessagingRowView> Items);

/// <summary>The pending scheduled deliveries of one store, or why the store cannot list them.</summary>
internal sealed record MessagingScheduledList(
    string Store,
    bool Supported,
    IReadOnlyList<ScheduledDeliveryView> Items,
    long TotalCount,
    string? Reason
);

/// <summary>One page of the operator ledger, newest first, and the cursor of the next page.</summary>
internal sealed record MessagingReceiptList(IReadOnlyList<MessagingOperationReceiptView> Items, string? NextCursor);

/// <summary>One consumer's circuit breaker, as the circuits view lists it.</summary>
/// <param name="Id">The circuit's object id, <c>{node}~{circuit key}</c>, which its commands act on.</param>
internal sealed record MessagingCircuitView(
    string Id,
    string Consumer,
    CircuitBreakerState State,
    int ConsecutiveFailures,
    int FailureThreshold,
    DateTimeOffset? OpenedAt,
    TimeSpan? EstimatedRemainingOpenDuration
);

internal sealed record MessagingCircuitList(IReadOnlyList<MessagingCircuitView> Items);

/// <summary>A node serving the module: this node, or one discovery lists.</summary>
/// <param name="Stores">The stores the node reads; empty for another node, which names its own.</param>
/// <param name="State">
/// What the last forwarded call to another node found, such as <c>not-answering</c>, or null before the first call
/// and for this node.
/// </param>
internal sealed record MessagingNodeView(string Name, bool IsLocal, IReadOnlyList<string> Stores, string? State = null);

internal sealed record MessagingNodeList(IReadOnlyList<MessagingNodeView> Items)
{
    /// <summary>Gets what the last discovery read found, or null when forwarding is off.</summary>
    public string? Discovery { get; init; }
}

/// <summary>
/// The last five minutes of this node's throughput, one entry per second. A second the node did not measure, such
/// as one before its listener started, is <see langword="null"/>, never zero.
/// </summary>
internal sealed record MessagingRealtimeMetrics(
    IReadOnlyList<long> Seconds,
    IReadOnlyList<int?> PublishedPerSecond,
    IReadOnlyList<int?> ConsumedPerSecond,
    IReadOnlyList<int?> ConsumeElapsedMs
);

/// <summary>The filter a received or published list sends, and a Needs-you group hands over.</summary>
internal sealed record MessagingListScope
{
    /// <summary>The node whose rows the filter selects; <see langword="null"/> for the node that runs it.</summary>
    public string? Node { get; init; }

    public string? Store { get; init; }

    public StatusName? Status { get; init; }

    public bool TerminalOnly { get; init; }

    public string? Consumer { get; init; }

    public string? Name { get; init; }

    public string? Fingerprint { get; init; }

    public MessageListQuery ToQuery(MessageType type) =>
        new()
        {
            MessageType = type,
            Status = Status,
            TerminalOnly = TerminalOnly,
            ConsumerIdentity = string.IsNullOrEmpty(Consumer) ? null : Consumer,
            Name = string.IsNullOrEmpty(Name) ? null : Name,
            FailureFingerprint = string.IsNullOrEmpty(Fingerprint) ? null : Fingerprint,
        };
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(MessagingRowView))]
[JsonSerializable(typeof(MessagingMessageDetail))]
[JsonSerializable(typeof(MessagingRowList))]
[JsonSerializable(typeof(MessagingGenerationList))]
[JsonSerializable(typeof(MessagingScheduledList))]
[JsonSerializable(typeof(MessagingReceiptList))]
[JsonSerializable(typeof(MessagingCircuitList))]
[JsonSerializable(typeof(MessagingNodeList))]
[JsonSerializable(typeof(MessagingRealtimeMetrics))]
[JsonSerializable(typeof(MessagingListScope))]
internal sealed partial class MessagingDashboardJsonContext : JsonSerializerContext;
