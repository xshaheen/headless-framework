// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Fencing;

/// <summary>
/// The provider seam behind fenced leases: each verb is one decision on one clock snapshot of the store, run either
/// on the provider's own connection (autonomous) or inside a unit of work (enlisted).
/// </summary>
/// <remarks>
/// <para>
/// Lease state is active, settled, released, or abandoned; "expired" is never stored. It means active with an expiry
/// at or before the database's clock, read inside the statement that decides. A generation is drawn from one
/// store-wide sequence only after the grant holds the lease row's lock, so every grant's generation is above every
/// earlier one for that key, including after the row was purged.
/// </para>
/// <para>
/// Autonomous verbs commit before returning; a relational provider opens its own connection at READ COMMITTED and
/// retries only deadlocks. Enlisted verbs run inside the unit (a relational provider on its connection and
/// transaction), never commit, and never retry. Argument, tenant, duration, and unit-state checks happen before a call
/// reaches the store, and every enlisted call is preceded by <see cref="ValidateEnlistment" />, which decides what kind
/// of unit the provider can write through. A provider package registers the implementation; application code never
/// calls it.
/// </para>
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ILeaseStore
{
    /// <summary>
    /// Begins an owned unit of work the enlisted verbs accept (for a relational provider, on a new connection to its
    /// configured database), so a sweep can claim a lease and run its handler in one unit the sweep commits.
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

    /// <summary>Grants the lease on the provider's own connection and commits before returning.</summary>
    /// <param name="key">The lease key.</param>
    /// <param name="duration">The lease's time to live from the database's clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>The grant's result; an expired active lease is taken over.</returns>
    ValueTask<LeaseGrantResult> GrantAsync(
        LeaseKey key,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Grants the lease inside <paramref name="unitOfWork" />, without committing. The row stays locked
    /// until that transaction ends.
    /// </summary>
    /// <param name="unitOfWork">The unit of work, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The lease key.</param>
    /// <param name="duration">The lease's time to live from the database's clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The grant's result; an expired active lease is taken over.</returns>
    ValueTask<LeaseGrantResult> GrantEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Renews the lease on the provider's own connection and commits before returning.</summary>
    /// <param name="key">The lease key.</param>
    /// <param name="generation">The caller's generation.</param>
    /// <param name="duration">The new time to live from the database's clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>The renewal's result; an absent row is <see cref="LeaseRenewalStatus.Stale" />.</returns>
    ValueTask<LeaseRenewalResult> RenewAsync(
        LeaseKey key,
        long generation,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Renews the lease inside <paramref name="unitOfWork" />, without committing.</summary>
    /// <param name="unitOfWork">The unit of work, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The lease key.</param>
    /// <param name="generation">The caller's generation.</param>
    /// <param name="duration">The new time to live from the database's clock.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The renewal's result; an absent row is <see cref="LeaseRenewalStatus.Stale" />.</returns>
    ValueTask<LeaseRenewalResult> RenewEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Settles the lease on the provider's own connection and commits before returning.</summary>
    /// <param name="key">The lease key.</param>
    /// <param name="generation">The caller's generation.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>
    /// <see cref="LeaseSettlementStatus.Settled" /> when the lease settled now or was already settled at this
    /// generation; otherwise the refusal. An absent row is <see cref="LeaseSettlementStatus.Stale" />.
    /// </returns>
    ValueTask<LeaseSettlementStatus> SettleAsync(
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    );

    /// <summary>Settles the lease inside <paramref name="unitOfWork" />, without committing.</summary>
    /// <param name="unitOfWork">The unit of work, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The lease key.</param>
    /// <param name="generation">The caller's generation.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The same outcomes as <see cref="SettleAsync" />.</returns>
    ValueTask<LeaseSettlementStatus> SettleEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    );

    /// <summary>Releases the lease on the provider's own connection and commits before returning.</summary>
    /// <param name="key">The lease key.</param>
    /// <param name="generation">The caller's generation.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>
    /// <see cref="LeaseSettlementStatus.Released" /> when the lease was released now or already released at this
    /// generation; otherwise the refusal. An absent row is <see cref="LeaseSettlementStatus.Stale" />.
    /// </returns>
    ValueTask<LeaseSettlementStatus> ReleaseAsync(
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    );

    /// <summary>Releases the lease inside <paramref name="unitOfWork" />, without committing.</summary>
    /// <param name="unitOfWork">The unit of work, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The lease key.</param>
    /// <param name="generation">The caller's generation.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The same outcomes as <see cref="ReleaseAsync" />.</returns>
    ValueTask<LeaseSettlementStatus> ReleaseEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads the lease row inside <paramref name="unitOfWork" /> with an update-intent lock that holds
    /// until the transaction ends, and reports whether <paramref name="generation" /> is current, active, and
    /// unexpired. A shared lock is not enough: a waiting grant would take the update lock first, and the fencing
    /// transaction's own later settle would deadlock against it.
    /// </summary>
    /// <param name="unitOfWork">The unit of work, already accepted by <see cref="ValidateEnlistment" />.</param>
    /// <param name="key">The lease key.</param>
    /// <param name="generation">The caller's generation.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>What the read found; an absent row is <see cref="LeaseFenceStatus.Stale" />.</returns>
    ValueTask<LeaseFenceStatus> FenceEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Claims one expired, active lease of <paramref name="kind" /> inside <paramref name="unitOfWork" />,
    /// skipping rows another transaction has locked, and marks it abandoned in the same statement. Leases are visited
    /// in <c>(expires_at, tenant_id, resource)</c> order, strictly after <paramref name="after" />.
    /// </summary>
    /// <param name="unitOfWork">An owned unit from <see cref="BeginOwnedUnitAsync" />.</param>
    /// <param name="kind">The lease kind to sweep.</param>
    /// <param name="after">
    /// The last lease this sweep call visited, or <see langword="null" /> to start from the earliest expiry. Its
    /// <see langword="null" /> tenant is the host scope's stored empty tenant.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The claimed lease, or <see langword="null" /> when none is left after the cursor.</returns>
    ValueTask<ExpiredLease?> ClaimExpiredEnlistedAsync(
        IUnitOfWork unitOfWork,
        string kind,
        ExpiredLease? after,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes, in bounded batches on the provider's own connection, the settled, released, and abandoned leases of
    /// <paramref name="kind" /> that ended at least <paramref name="olderThan" /> before the database's clock.
    /// </summary>
    /// <param name="kind">The lease kind to purge.</param>
    /// <param name="olderThan">How long a lease must have been over before it is deleted.</param>
    /// <param name="cancellationToken">Token used to cancel the database calls.</param>
    /// <returns>The number of rows deleted.</returns>
    ValueTask<int> PurgeAsync(string kind, TimeSpan olderThan, CancellationToken cancellationToken = default);
}
