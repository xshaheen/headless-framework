// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;

namespace Headless.Messaging.Dashboard;

#pragma warning disable MA0048 // A topic file: the trace rows, a node's part, and the merge that turns them into the module's answer.

/// <summary>
/// Merges the rows every store and node read for one correlation id into the module's trace answer: each row once,
/// the oldest <see cref="MessagingDashboardOptions.TraceLimit"/>, and each causation typed against every row read.
/// </summary>
internal static class MessagingTrace
{
    /// <summary>Returns the module's trace answer from the parts this node and each peer read, this node's first.</summary>
    public static DashboardTraceAnswer Answer(string correlationId, int limit, IReadOnlyList<MessagingTracePart> parts)
    {
        var seen = new HashSet<(MessageType, string, Guid)>();
        var unique = new List<MessagingTraceRow>();
        var unavailable = new List<string>();

        foreach (var part in parts)
        {
            unavailable.AddRange(part.UnavailableSources);

            foreach (var row in part.Rows)
            {
                // Nodes that share a store return the same rows; the first one read keeps its node label.
                if (seen.Add((row.Type, row.Store, row.StorageId)))
                {
                    unique.Add(row);
                }
            }
        }

        // A stored causation is a bare id: it names a message when a published or received row of this correlation
        // carries that id, and otherwise a job run, the only other kind of object that publishes.
        var messageIds = unique.Select(static row => row.MessageId).ToHashSet(StringComparer.Ordinal);

        // The order each store cut its own rows by, so the merged cut keeps the same oldest rows.
        var kept = unique
            .OrderBy(static row => row.Added)
            .ThenBy(static row => row.StorageId)
            .Take(limit)
            .Select(row => _Item(row, correlationId, messageIds));

        return new DashboardTraceAnswer
        {
            Items = [.. kept],
            UnavailableSources = [.. unavailable.Distinct(StringComparer.Ordinal)],
            Limited = unique.Count > limit,
            ListLink = new DashboardTraceListLink
            {
                Path = MessagingViews.PublishedKind,
                State = JsonSerializer.SerializeToElement(
                    new MessagingSearchRequest { CorrelationId = correlationId },
                    MessagingDashboardJsonContext.Default.MessagingSearchRequest
                ),
            },
        };
    }

    /// <summary>The rows of one store's trace read, each labelled with this node and the store.</summary>
    public static IEnumerable<MessagingTraceRow> Rows(string node, MessagingStore store, MessageTraceResult result)
    {
        var published = result.Published.Select(row => _Row(MessageType.Publish, node, store.Name, row));

        // Only the main store's received rows open as objects; a store without a received table returns none anyway.
        return store.HoldsReceived
            ? published.Concat(result.Received.Select(row => _Row(MessageType.Subscribe, node, store.Name, row)))
            : published;
    }

    private static MessagingTraceRow _Row(MessageType type, string node, string store, MessageTraceRow row) =>
        new()
        {
            Type = type,
            Node = node,
            Store = store,
            StorageId = row.StorageId,
            MessageId = row.MessageId,
            Name = row.Name,
            CausationId = row.CausationId,
            Added = row.Added,
            Status = row.Status,
            DueAt = row.DueAt,
        };

    private static DashboardTraceItem _Item(MessagingTraceRow row, string correlationId, HashSet<string> messageIds)
    {
        var received = row.Type == MessageType.Subscribe;
        var storageId = row.StorageId.ToString("D");

        return new DashboardTraceItem
        {
            Target = MessagingViews.Ref(row.Type, new MessagingObjectId(row.Node, row.Store, row.StorageId)),
            Store = row.Store,
            StorageId = storageId,
            Title = row.Name,
            Status = row.Status.ToString(),
            StatusRole = _Role(row.Status),
            At = row.DueAt ?? row.Added,
            TimestampKind = row.DueAt is null ? DashboardTraceTimestampKind.Added : DashboardTraceTimestampKind.Due,
            Node = row.Node,
            CorrelationId = correlationId,
            // A published message is caused by its message id; a received generation, one per delivery or replay, by
            // its own row.
            Key = received
                ? new DashboardTraceKey { Kind = DashboardTraceKeyKind.Delivery, Value = storageId }
                : new DashboardTraceKey { Kind = DashboardTraceKeyKind.Message, Value = row.MessageId },
            Causation = row.CausationId is { } causation
                ? new DashboardTraceKey
                {
                    Kind = messageIds.Contains(causation) ? DashboardTraceKeyKind.Message : DashboardTraceKeyKind.Run,
                    Value = causation,
                }
                : null,
            DeliversMessageId = received ? row.MessageId : null,
        };
    }

    // The roles the module's own lists paint each status in.
    private static string _Role(StatusName status) =>
        status switch
        {
            StatusName.Failed => "fail",
            StatusName.Succeeded => "ok",
            StatusName.Queued => "enq",
            _ => "sched",
        };
}

/// <summary>One published or received row of a trace, labelled with the node and store that read it.</summary>
internal sealed record MessagingTraceRow
{
    public required MessageType Type { get; init; }

    public required string Node { get; init; }

    public required string Store { get; init; }

    public required Guid StorageId { get; init; }

    public required string MessageId { get; init; }

    public required string Name { get; init; }

    public string? CausationId { get; init; }

    public required DateTimeOffset Added { get; init; }

    public required StatusName Status { get; init; }

    public DateTimeOffset? DueAt { get; init; }
}

/// <summary>What one node read for a trace: its rows, and the sources that did not answer.</summary>
internal sealed record MessagingTracePart
{
    public static MessagingTracePart Empty { get; } = new() { Rows = [] };

    public required IReadOnlyList<MessagingTraceRow> Rows { get; init; }

    public IReadOnlyList<string> UnavailableSources { get; init; } = [];

    public static MessagingTracePart Unavailable(string source) => new() { Rows = [], UnavailableSources = [source] };
}

/// <summary>The body of a forwarded trace read: the id travels here, never in the URL.</summary>
internal sealed record PeerTraceBody(string? CorrelationId, int? Limit);

#pragma warning restore MA0048
