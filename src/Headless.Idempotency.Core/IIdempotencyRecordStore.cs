// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Idempotency;

/// <summary>
/// The provider seam behind durable idempotency: the record table, read and written inside a unit of work's
/// transaction so each record change commits or rolls back with the fenced-lease change beside it.
/// </summary>
/// <remarks>
/// <para>
/// A record is one row per <see cref="IdempotencyRecordKey" /> holding the fingerprint and its algorithm tag, the
/// admitted attempt's lease generation, the state (<see cref="IdempotencyRecordStatus" />), the result bytes and
/// contract, and <c>retention_until</c>. Every time comparison uses the database clock read inside the statement that
/// decides, never the application clock.
/// </para>
/// <para>
/// Every enlisted verb runs on the resource's connection and transaction, never commits, and never retries; each is
/// preceded by <see cref="ValidateEnlistment" />. Every verb that reads or writes a record takes, or already holds, an
/// update-intent row lock that lasts until the transaction ends, and the caller always locks the record before it
/// touches the record's fenced lease, so admissions, fences, completions, and releases of one key serialize without
/// deadlocking. Every write that sets <c>retention_until</c> sets it to the later of its current value and the
/// database clock plus the given retention, so retention is only ever extended. Argument, tenant, and unit-state
/// checks happen before a call reaches the store. A provider package registers the implementation; application code
/// never calls it.
/// </para>
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IIdempotencyRecordStore
{
    /// <summary>
    /// Begins an owned unit of work on a new connection to the provider's configured database, so an autonomous call
    /// can change the record and its lease in one transaction it commits.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel opening the connection and beginning the transaction.</param>
    /// <returns>The begun unit; its resource is relational and owned.</returns>
    ValueTask<IUnitOfWork> BeginOwnedUnitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws when <paramref name="resource" /> cannot host this provider's commands: its transaction belongs to
    /// another provider, it targets a different database than the one configured, or its connection is not open.
    /// </summary>
    /// <param name="resource">The unit of work's relational resource.</param>
    /// <exception cref="InvalidOperationException">The resource cannot host the command.</exception>
    void ValidateEnlistment(IRelationalUnitOfWorkResource resource);

    /// <summary>
    /// Locks the record for update, inserting it first when absent, and returns it. Never raises a unique-key
    /// violation, even when concurrent callers insert the same key: a losing insert waits for and then locks the
    /// winner's row.
    /// </summary>
    /// <remarks>
    /// An inserted row is <see cref="IdempotencyRecordStatus.Pending" /> with <paramref name="fingerprint" />, no
    /// lease generation, no result, and <c>retention_until</c> at the database clock plus
    /// <paramref name="retention" />. An existing row is returned unchanged.
    /// </remarks>
    /// <param name="resource">The unit of work's relational resource, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The record key.</param>
    /// <param name="fingerprint">The fingerprint an inserted row carries.</param>
    /// <param name="retention">The inserted row's retention from the database clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns>The locked record.</returns>
    ValueTask<IdempotencyRecordState> LockOrInsertAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    );

    /// <summary>Locks an existing record for update and returns it, without inserting.</summary>
    /// <param name="resource">The unit of work's relational resource, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The record key.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The locked record, or <see langword="null" /> when none exists.</returns>
    ValueTask<IdempotencyRecordState?> LockAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Records a new admitted attempt on the record this transaction already locked: state
    /// <see cref="IdempotencyRecordStatus.Pending" />, <paramref name="fingerprint" /> and its algorithm,
    /// <paramref name="leaseGeneration" />, no result or contract, and retention extended. A record past its retention
    /// is reset in place this way.
    /// </summary>
    /// <param name="resource">The unit of work's relational resource, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The record key.</param>
    /// <param name="fingerprint">The admitted request's fingerprint.</param>
    /// <param name="leaseGeneration">The admitted attempt's lease generation.</param>
    /// <param name="retention">The retention to extend to, from the database clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>A task that completes when the row is written.</returns>
    ValueTask AdmitAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        long leaseGeneration,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores the result on the record this transaction already locked, whose lease generation is
    /// <paramref name="leaseGeneration" />: state <see cref="IdempotencyRecordStatus.Completed" />, the result bytes
    /// and contract, and retention extended. The caller settled the lease in the same transaction first.
    /// </summary>
    /// <param name="resource">The unit of work's relational resource, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The record key.</param>
    /// <param name="leaseGeneration">The completing attempt's lease generation.</param>
    /// <param name="result">The result bytes.</param>
    /// <param name="contract">The result's contract tag.</param>
    /// <param name="retention">The retention to extend to, from the database clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>A task that completes when the row is written.</returns>
    ValueTask CompleteAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        long leaseGeneration,
        ReadOnlyMemory<byte> result,
        string contract,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Marks the record this transaction already locked, whose lease generation is
    /// <paramref name="leaseGeneration" />, as free for the next admission: state
    /// <see cref="IdempotencyRecordStatus.Pending" /> with no lease generation, and retention extended. The caller
    /// released the lease in the same transaction first.
    /// </summary>
    /// <param name="resource">The unit of work's relational resource, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The record key.</param>
    /// <param name="leaseGeneration">The releasing attempt's lease generation.</param>
    /// <param name="retention">The retention to extend to, from the database clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>A task that completes when the row is written.</returns>
    ValueTask ReleaseAsync(
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordKey key,
        long leaseGeneration,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes, on the provider's own connection and committed before returning, at most <paramref name="limit" />
    /// records, completed or pending, whose <c>retention_until</c> is at least <paramref name="olderThan" /> before the
    /// database clock. A record another transaction holds is skipped for a later purge rather than waited on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pending record past its retention is deleted even when its attempt may still be running, because retention is
    /// extended on every admission and far outlasts any lease. Such an attempt cannot store an outcome afterwards: its
    /// completion finds no record at its lease generation and is refused as stale, and a new admission of the key
    /// inserts a fresh record but still sees the attempt's live lease and reports it in flight.
    /// </para>
    /// <para>
    /// Only record rows are deleted; fenced-lease rows are never read or deleted here. The retention service purges
    /// those through the fencing API.
    /// </para>
    /// </remarks>
    /// <param name="olderThan">How long past its retention a record must be before it is deleted; zero or more.</param>
    /// <param name="limit">The most rows this call deletes; positive.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>The number of rows deleted.</returns>
    ValueTask<int> PurgeAsync(TimeSpan olderThan, int limit, CancellationToken cancellationToken = default);
}
