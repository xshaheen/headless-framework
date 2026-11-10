// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Messaging;

/// <summary>
/// Read-only query surface for the messaging dashboard and operator tooling.
/// Exposes message row lookups, aggregate statistics, and hourly throughput histograms.
/// </summary>
/// <remarks>
/// Resolved from DI via <c>IDataStorage.GetMonitoringApi()</c>. All queries operate on the
/// same underlying storage tables as the outbox and retry processors; results reflect the live
/// state of those tables at query time.
/// </remarks>
[PublicAPI]
public interface IMonitoringApi
{
    /// <summary>
    /// Returns the published message row with the given storage id, or <see langword="null"/> when not found.
    /// </summary>
    /// <param name="storageId">The internal storage row identifier.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<MediumMessage?> GetPublishedMessageAsync(Guid storageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the published message rows for the specified storage ids.
    /// Rows not found are omitted from the result; the result order may differ from input order.
    /// </summary>
    /// <param name="storageIds">A list of internal storage row identifiers to retrieve.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<IReadOnlyList<MediumMessage>> GetPublishedMessagesAsync(
        IReadOnlyList<Guid> storageIds,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the received message row with the given storage id, or <see langword="null"/> when not found.
    /// </summary>
    /// <param name="storageId">The internal storage row identifier.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<MediumMessage?> GetReceivedMessageAsync(Guid storageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the received message rows for the specified storage ids.
    /// Rows not found are omitted from the result; the result order may differ from input order.
    /// </summary>
    /// <param name="storageIds">A list of internal storage row identifiers to retrieve.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<IReadOnlyList<MediumMessage>> GetReceivedMessagesAsync(
        IReadOnlyList<Guid> storageIds,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns aggregate statistics across both the published and received message tables.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns how many published and received rows are still waiting and how long the oldest of each has waited, in
    /// one read of the storage. <see cref="BacklogView"/> defines which rows count.
    /// </summary>
    /// <remarks>
    /// The messaging backlog sampler (<see cref="MessagingOptions.BacklogSamplingInterval"/>) calls this once per
    /// tick.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<BacklogView> GetBacklogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paged list of message rows matching the supplied query filters.
    /// </summary>
    /// <param name="query">Filters and pagination parameters for the message listing.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<IndexPage<MessageView>> GetMessagesAsync(
        MessageQuery query,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns at most <paramref name="pageSize"/> rows of one table that <paramref name="query"/> matches, newest
    /// first by insert time and then by storage identifier, starting after <paramref name="after"/>.
    /// </summary>
    /// <param name="query">The rows to read.</param>
    /// <param name="after">The <see cref="MessageListPage.Next"/> of the previous page, or <see langword="null"/> for the first page.</param>
    /// <param name="pageSize">The most rows to return, at most 500.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    /// <returns>The page; its rows carry no content.</returns>
    /// <remarks>
    /// The keyset is (added, id), which no write changes, so paging never repeats or skips a row that changes state
    /// between pages. Rows with an unrecognized lane are never listed.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageSize"/> is not between 1 and 500.</exception>
    ValueTask<MessageListPage> ListMessagesAsync(
        MessageListQuery query,
        MessageListPosition? after,
        int pageSize,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Counts the rows <paramref name="query"/> matches, stopping at <paramref name="cap"/>: a larger count reports the
    /// cap with <see cref="MessageCount.Capped"/> set.
    /// </summary>
    /// <param name="query">
    /// The rows to count. Status <see cref="StatusName.Scheduled"/>, <see cref="StatusName.Queued"/>, terminal
    /// <see cref="StatusName.Failed"/>, and <see cref="MessageListQuery.IsHeld"/> are the dashboard's counts.
    /// </param>
    /// <param name="cap">The cap.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    /// <returns>The capped count.</returns>
    /// <remarks>The store reads at most one row past the cap, so a count costs no more than the cap.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is not positive.</exception>
    ValueTask<MessageCount> CountMessagesAsync(
        MessageListQuery query,
        int cap = MessageCount.DefaultCap,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the failures of one table that ended for good, grouped by failure fingerprint and, for received rows,
    /// by consumer, newest group first.
    /// </summary>
    /// <param name="messageType">The table to read.</param>
    /// <param name="consumerIdentity">The one consumer to group, or <see langword="null"/> for every consumer.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    /// <returns>Every group.</returns>
    /// <remarks>
    /// A failure counts while it is <see cref="StatusName.Failed"/> with no retry pending and, for an inbox
    /// generation, while it is the current generation: a re-executed generation leaves its group. Consumers are told
    /// apart by the bytes of their identity, so two identities that differ only by trailing spaces form two groups on
    /// every store.
    /// </remarks>
    ValueTask<IReadOnlyList<MessageFailureGroup>> GetFailureGroupsAsync(
        MessageType messageType,
        string? consumerIdentity = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns a bounded, deterministic page of rows whose persisted lane value is not recognized.
    /// The query never deserializes message content and does not mutate the returned rows.
    /// </summary>
    /// <param name="query">The table direction and one-based pagination parameters.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<IndexPage<UnknownLaneMessageView>> GetUnknownLaneMessagesAsync(
        UnknownLaneMessageQuery query,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the total number of published message rows in a failed state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<long> GetPublishedFailedCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total number of published message rows in a succeeded state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<long> GetPublishedSucceededCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total number of received message rows in a failed state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<long> GetReceivedFailedCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total number of received message rows in a succeeded state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<long> GetReceivedSucceededCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns per-hour succeeded message counts for the last 24 hours, keyed by UTC hour bucket
    /// (a <see cref="DateTimeOffset"/> with zero offset, truncated to the start of the hour).
    /// </summary>
    /// <param name="type">Whether to query the published or received message table.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> GetHourlySucceededJobsAsync(
        MessageType type,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns per-hour failed message counts for the last 24 hours, keyed by UTC hour bucket
    /// (a <see cref="DateTimeOffset"/> with zero offset, truncated to the start of the hour).
    /// </summary>
    /// <param name="type">Whether to query the published or received message table.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> GetHourlyFailedJobsAsync(
        MessageType type,
        CancellationToken cancellationToken = default
    );
}
