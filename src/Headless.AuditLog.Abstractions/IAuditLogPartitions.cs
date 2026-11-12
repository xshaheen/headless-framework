// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Keeps the monthly partitions of a partitioned audit log table: creates the months ahead, and enforces retention by
/// dropping whole months instead of deleting rows.
/// </summary>
/// <typeparam name="TContext">
/// The persistence context type that owns the audit log table, matching <see cref="IAuditLogRetention{TContext}"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// PostgreSQL only. The table is partitioned by <c>AuditLogStorageOptions.Partitioning</c> on the PostgreSQL provider,
/// or by the application's migration, with <c>migrationBuilder.AddHeadlessPostgreSqlAuditLogPartitions(...)</c>, on
/// Entity Framework storage. Both install the
/// database functions this service calls; the functions run with their owner's rights, so the calling login needs
/// only <c>EXECUTE</c> on them and no DDL rights.
/// </para>
/// <para>
/// A row whose <c>CreatedAt</c> falls in no partition fails its insert, so call <see cref="EnsureAsync"/> from a
/// scheduled job at least once a month. Call <see cref="DropExpiredAsync"/> from the same job to enforce retention.
/// </para>
/// </remarks>
public interface IAuditLogPartitions<TContext>
{
    /// <summary>
    /// Creates every missing monthly partition from the current month, by the registered <see cref="TimeProvider"/>,
    /// through <c>AuditLogStorageOptions.PartitionMonthsAhead</c> months after it. When the table is append-only, it
    /// also adds the <c>TRUNCATE</c> guard to every partition that lacks one.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of partitions created.</returns>
    /// <exception cref="InvalidOperationException">The storage is not configured for a partitioned table.</exception>
    /// <exception cref="NotSupportedException">The database is not PostgreSQL.</exception>
    Task<int> EnsureAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops every partition whose whole range ended more than <paramref name="olderThan"/> ago. A month that still
    /// holds one entry inside the retention period stays whole.
    /// </summary>
    /// <param name="olderThan">The retention period, measured back from the registered <see cref="TimeProvider"/>'s current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of partitions dropped.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="olderThan"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The storage is not configured for a partitioned table.</exception>
    /// <exception cref="NotSupportedException">The database is not PostgreSQL.</exception>
    Task<int> DropExpiredAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
}
