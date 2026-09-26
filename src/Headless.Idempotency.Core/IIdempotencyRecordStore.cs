// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Idempotency;

/// <summary>
/// The provider seam behind durable idempotency: the record table, whose rows carry their own lease, read and written
/// inside a unit of work.
/// </summary>
/// <remarks>
/// <para>
/// A record is one row per <see cref="IdempotencyRecordKey" /> holding the fingerprint and its algorithm tag, the
/// state (<see cref="IdempotencyRecordStatus" />), the admitted attempt's generation and lease expiry, the result bytes
/// and contract, and <c>retention_until</c>. Generations come from one store-wide sequence the provider's initializer
/// creates, drawn only after the record's row lock is held, so they grow per key even across a purge of the row. Every
/// time comparison uses the database clock read after the row lock is held, never the application clock.
/// </para>
/// <para>
/// Every enlisted verb runs inside the unit (a relational provider on its connection and transaction), never commits,
/// and never retries; each is preceded by <see cref="ValidateEnlistment" />, which decides what kind of unit the
/// provider can write through. Every verb that reads or writes a record takes, or already holds, an
/// update-intent row lock that lasts until the transaction ends, so admissions, fences, renewals, completions, and
/// releases of one key serialize on that single row. Every write that sets <c>retention_until</c> sets it to the later
/// of its current value and the database clock plus the given retention, so retention is only ever extended. Argument,
/// tenant, and unit-state checks happen before a call reaches the store. A provider package registers the
/// implementation; application code never calls it.
/// </para>
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IIdempotencyRecordStore
{
    /// <summary>
    /// Begins an owned unit of work the enlisted verbs accept (for a relational provider, on a new connection to its
    /// configured database), so an autonomous call can change the record in a unit it commits.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel opening the connection and beginning the transaction.</param>
    /// <returns>The begun unit, which <see cref="ValidateEnlistment" /> accepts.</returns>
    ValueTask<IUnitOfWork> BeginOwnedUnitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws when <paramref name="unitOfWork" /> cannot host this provider's commands. A relational provider refuses a
    /// unit without a live transaction of its own provider, on a different database than the one configured, or on a
    /// connection that is not open; an in-process provider refuses a unit whose work commits in a database.
    /// </summary>
    /// <param name="unitOfWork">The active unit of work.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the command.</exception>
    void ValidateEnlistment(IUnitOfWork unitOfWork);

    /// <summary>
    /// Locks the record for update, inserting it first when absent, and returns it. Never raises a unique-key
    /// violation, even when concurrent callers insert the same key: a losing insert waits for and then locks the
    /// winner's row.
    /// </summary>
    /// <remarks>
    /// An inserted row is <see cref="IdempotencyRecordStatus.Pending" /> with <paramref name="fingerprint" />, no
    /// generation or lease, no result, and <c>retention_until</c> at the database clock plus
    /// <paramref name="retention" />. An existing row is returned unchanged.
    /// </remarks>
    /// <param name="unitOfWork">The unit of work, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The record key.</param>
    /// <param name="fingerprint">The fingerprint an inserted row carries.</param>
    /// <param name="retention">The inserted row's retention from the database clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns>The locked record.</returns>
    ValueTask<IdempotencyRecordState> LockOrInsertAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Locks an existing record for update and returns it, without inserting. The lock is update-intent, never shared,
    /// and lasts until the transaction ends: a fence holds it while its unit writes, and an admission waiting behind a
    /// shared lock would deadlock against that unit's own completion.
    /// </summary>
    /// <param name="unitOfWork">The unit of work, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The record key.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The locked record, or <see langword="null" /> when none exists.</returns>
    ValueTask<IdempotencyRecordState?> LockAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Admits a new attempt on the record this transaction already locked: draws the next generation from the store's
    /// sequence, sets the lease to expire <paramref name="leaseDuration" /> after the database clock, and writes state
    /// <see cref="IdempotencyRecordStatus.Pending" />, <paramref name="fingerprint" /> and its algorithm, no result or
    /// contract, and retention extended. A record past its retention is reset in place this way.
    /// </summary>
    /// <param name="unitOfWork">The unit of work, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The record key.</param>
    /// <param name="fingerprint">The admitted request's fingerprint.</param>
    /// <param name="leaseDuration">How long the admitted attempt owns the key, from the database clock.</param>
    /// <param name="retention">The retention to extend to, from the database clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The granted generation and lease expiry.</returns>
    ValueTask<IdempotencyRecordGrant> AdmitAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        TimeSpan leaseDuration,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores the result on the record this transaction already locked and found held by
    /// <paramref name="generation" />: state <see cref="IdempotencyRecordStatus.Completed" />, the result bytes and
    /// contract, no lease expiry (the generation is kept), and retention extended.
    /// </summary>
    /// <param name="unitOfWork">The unit of work, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The record key.</param>
    /// <param name="generation">The completing attempt's generation.</param>
    /// <param name="result">The result bytes.</param>
    /// <param name="contract">The result's contract tag.</param>
    /// <param name="retention">The retention to extend to, from the database clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>A task that completes when the row is written.</returns>
    ValueTask CompleteAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        long generation,
        ReadOnlyMemory<byte> result,
        string contract,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Frees the record this transaction already locked and found held by <paramref name="generation" /> for the next
    /// admission: state <see cref="IdempotencyRecordStatus.Pending" /> with no generation or lease, and retention
    /// extended.
    /// </summary>
    /// <param name="unitOfWork">The unit of work, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The record key.</param>
    /// <param name="generation">The releasing attempt's generation.</param>
    /// <param name="retention">The retention to extend to, from the database clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>A task that completes when the row is written.</returns>
    ValueTask ReleaseAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        long generation,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Extends the lease of the attempt that holds <paramref name="generation" /> to <paramref name="leaseDuration" />
    /// after the database clock, on the provider's own connection and committed before returning. One guarded update
    /// under the row's update-intent lock: it applies only while the record is pending at that generation with a live
    /// lease, and otherwise reports why, writing nothing. A deadlock is retried in a fresh transaction.
    /// </summary>
    /// <param name="key">The record key.</param>
    /// <param name="generation">The renewing attempt's generation.</param>
    /// <param name="leaseDuration">The lease's new time to live, from the database clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>The renewal's result.</returns>
    ValueTask<IdempotentLeaseRenewal> RenewAsync(
        IdempotencyRecordKey key,
        long generation,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads <paramref name="key" />'s status on the provider's own connection, without the row lock or the unit of
    /// work every other verb takes: no <c>FOR UPDATE</c>/<c>UPDLOCK</c>, so it never waits behind a concurrent
    /// admission, fence, completion, or release the way <see cref="LockAsync" /> would.
    /// </summary>
    /// <param name="key">The record key.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>
    /// <see cref="IdempotencyPeekStatus.Absent" /> when the row does not exist or its <c>retention_until</c> is at or
    /// before the database clock; otherwise the row's <see cref="IdempotencyRecordStatus" /> mapped to
    /// <see cref="IdempotencyPeekStatus.Pending" /> or <see cref="IdempotencyPeekStatus.Completed" />.
    /// </returns>
    ValueTask<IdempotencyPeekStatus> PeekAsync(IdempotencyRecordKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes, on the provider's own connection and committed before returning, at most <paramref name="limit" />
    /// records whose <c>retention_until</c> is at least <paramref name="olderThan" /> before the database clock and
    /// whose lease, if any, is no longer live. A record another transaction holds is skipped for a later purge rather
    /// than waited on.
    /// </summary>
    /// <remarks>
    /// A pending record whose attempt renewed its lease past the retention is kept until that lease expires, so a live
    /// attempt never loses the record it will complete. Generations come from the store-wide sequence, so a key
    /// admitted again after its record was purged still gets a generation above every earlier attempt's, and a zombie
    /// attempt of the purged record can never match it.
    /// </remarks>
    /// <param name="olderThan">How long past its retention a record must be before it is deleted; zero or more.</param>
    /// <param name="limit">The most rows this call deletes; positive.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>The number of rows deleted.</returns>
    ValueTask<int> PurgeAsync(TimeSpan olderThan, int limit, CancellationToken cancellationToken = default);
}
