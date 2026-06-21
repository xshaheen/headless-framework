// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Primitives;

namespace Headless.Messaging.Monitoring;

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
    /// Returns a paged list of message rows matching the supplied query filters.
    /// </summary>
    /// <param name="query">Filters and pagination parameters for the message listing.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<IndexPage<MessageView>> GetMessagesAsync(
        MessageQuery query,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the total number of published message rows in a failed state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<long> PublishedFailedCount(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total number of published message rows in a succeeded state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<long> PublishedSucceededCount(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total number of received message rows in a failed state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<long> ReceivedFailedCount(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total number of received message rows in a succeeded state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<long> ReceivedSucceededCount(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns per-hour succeeded message counts for the last 24 hours, keyed by UTC hour bucket.
    /// </summary>
    /// <param name="type">Whether to query the published or received message table.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<Dictionary<DateTime, int>> HourlySucceededJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns per-hour failed message counts for the last 24 hours, keyed by UTC hour bucket.
    /// </summary>
    /// <param name="type">Whether to query the published or received message table.</param>
    /// <param name="cancellationToken">A token to cancel the query.</param>
    ValueTask<Dictionary<DateTime, int>> HourlyFailedJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    );
}
