// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>
/// Reads the stored counters for an operator view, such as a dashboard: their values, partitions, policies, last
/// formatted numbers, and next resets. Every call is read-only: it never issues, reserves, or changes a number.
/// </summary>
/// <remarks>
/// <para>
/// Inspection reads across every tenant and ignores <c>ICurrentTenant</c>: filter by
/// <see cref="SequenceCounterQuery.TenantId" /> instead. It shows every tenant's counters, so put the calls behind
/// operator authorization.
/// </para>
/// <para>
/// Each call is one statement with no transaction, so it does not wait on a gap-free unit holding a counter's row. On
/// PostgreSQL and SQLite a held row shows its last committed value; SQL Server skips it, so a page or a count can miss
/// a counter an open unit is writing. SQL Server still waits behind a page or table lock, and SQLite under its default rollback
/// journal waits while a writer commits; WAL mode removes that wait.
/// </para>
/// <para>
/// Never call <c>NextAsync</c> or <c>ReserveAsync</c> to learn a counter's value: each takes a number, and for a
/// gap-free counter that leaves a real gap.
/// </para>
/// </remarks>
[PublicAPI]
public interface ISequenceInspector
{
    /// <summary>Lists one page of counters in key order (tenant, name, partition).</summary>
    /// <param name="query">The filters, the page size, and the key to continue after.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The page, and the key to pass as <see cref="SequenceCounterQuery.After" /> for the next one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">A tenant id, name, partition, or search term is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="SequenceCounterQuery.Limit" /> is outside 1 to 200.</exception>
    ValueTask<SequenceCounterPage> ListCountersAsync(
        SequenceCounterQuery query,
        CancellationToken cancellationToken = default
    );

    /// <summary>Lists one page of the partitions of one counter name in one tenant, newest partition first.</summary>
    /// <param name="query">The tenant, the name, the page size, and the partition to continue before.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The page, and the partition to pass as <see cref="SequencePartitionQuery.Before" /> for the next one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">The tenant id, name, or partition is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="SequencePartitionQuery.Limit" /> is outside 1 to 200.</exception>
    ValueTask<SequencePartitionPage> ListPartitionsAsync(
        SequencePartitionQuery query,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Counts the counters the filters match, in total and by mode, and returns the next reset of the registered
    /// policies. Counting stops at 10,000 counters.
    /// </summary>
    /// <param name="query">The filters.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The counts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">A tenant id, name, or search term is invalid.</exception>
    ValueTask<SequenceCounterSummary> GetSummaryAsync(
        SequenceSummaryQuery query,
        CancellationToken cancellationToken = default
    );
}
