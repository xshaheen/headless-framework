// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.

/// <summary>Which rows <see cref="IMonitoringApi.GetCorrelatedMessagesAsync"/> reads.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record MessageTraceQuery
{
    /// <summary>
    /// Gets the correlation id (<see cref="Headers.CorrelationId"/>) every row read must carry. It matches exactly and
    /// ordinally on every store: <c>C-42</c>, <c>c-42</c> and <c>c-42 </c> are three different ids.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Gets how many rows of each table the caller keeps. A store reads the oldest <c>Limit + 1</c> rows of each, so
    /// the caller sees a table hold more than it asked for when it gets the extra row.
    /// </summary>
    public required int Limit { get; init; }
}

/// <summary>
/// The rows of one correlation id, published and received apart, each oldest first by the time it was added and then
/// by storage identifier.
/// </summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record MessageTraceResult
{
    /// <summary>Gets the published rows, at most <see cref="MessageTraceQuery.Limit"/> plus one.</summary>
    public required IReadOnlyList<MessageTraceRow> Published { get; init; }

    /// <summary>
    /// Gets every generation of the received rows, at most <see cref="MessageTraceQuery.Limit"/> plus one. A store
    /// that holds no received table, such as an additional outbox, returns none.
    /// </summary>
    public required IReadOnlyList<MessageTraceRow> Received { get; init; }
}

/// <summary>One published or received row of a correlation id.</summary>
/// <remarks>
/// A published row is identified in a causal chain by <see cref="MessageId"/>: a message published inside a consumer
/// records the consumed message's id as its <see cref="CausationId"/>. A received generation is identified by
/// <see cref="StorageId"/>, because one message delivered to several consumers, or replayed, has one row per delivery.
/// This record grows by <see langword="init"/> members.
/// </remarks>
[PublicAPI]
public sealed record MessageTraceRow
{
    /// <summary>Gets the internal storage row identifier.</summary>
    public required Guid StorageId { get; init; }

    /// <summary>
    /// Gets the application-assigned message id. On a received row it is the id of the published message the row
    /// delivers.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>Gets the message name (topic or queue name).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the causation id the message carries (<see cref="Headers.CausationId"/>), or <see langword="null"/> when it
    /// has none or one longer than the store keeps.
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>Gets when the row was inserted into storage.</summary>
    public required DateTimeOffset Added { get; init; }

    /// <summary>Gets the row's status.</summary>
    public required StatusName Status { get; init; }

    /// <summary>
    /// Gets when the row is due while it is waiting: the publish time of a published row that is
    /// <see cref="StatusName.Delayed"/> or <see cref="StatusName.Queued"/>, otherwise its next retry or dispatch time.
    /// <see langword="null"/> when nothing is pending.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }
}

/// <summary>The rules every store applies when it projects a row into a <see cref="MessageTraceRow"/>.</summary>
internal static class MessageTraceRows
{
    /// <summary>
    /// A published row that has not been sent holds its publish time in the expiry column; every other row's expiry is
    /// a retention time and says nothing about when it is due.
    /// </summary>
    public static DateTimeOffset? DueAt(
        bool received,
        StatusName status,
        DateTimeOffset? expiresAt,
        DateTimeOffset? nextRetryAt
    )
    {
        return !received && (status is StatusName.Delayed or StatusName.Queued)
            ? expiresAt ?? nextRetryAt
            : nextRetryAt;
    }
}

#pragma warning restore MA0048
