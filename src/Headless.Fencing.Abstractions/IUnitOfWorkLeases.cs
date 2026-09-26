// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Fencing;

/// <summary>
/// The enlisted lease surface behind <c>unit.Leases</c>: every call runs inside the unit's own transaction, so the
/// unit's commit makes it durable and its rollback undoes it.
/// </summary>
/// <remarks>
/// A singleton registered by <c>AddHeadlessFencing</c>. It holds no unit: the caller's handle arrives per call, and
/// the call refuses, before any command runs, a unit that is no longer active, carries no relational resource,
/// carries a completed transaction, or carries a transaction the configured provider cannot write through. Enlisted
/// calls are never retried. Application code reaches it through <c>unit.Leases</c> rather than directly.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUnitOfWorkLeases : IUnitOfWorkFeature
{
    /// <summary>Grants the lease inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the grant.</param>
    /// <param name="kind">The lease kind.</param>
    /// <param name="resource">The leased resource within the kind.</param>
    /// <param name="duration">How long the lease lives unless renewed; bounded by the configured limits.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The grant's result.</returns>
    /// <exception cref="ArgumentException">The kind, resource, or current tenant id is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is outside the configured bounds.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    ValueTask<LeaseGrantResult> GrantAsync(
        IUnitOfWork unitOfWork,
        string kind,
        string resource,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Renews the lease inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the renewal.</param>
    /// <param name="lease">The lease to renew.</param>
    /// <param name="duration">The new time to live; bounded by the configured limits.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The renewal's result.</returns>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is outside the configured bounds.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    ValueTask<LeaseRenewalResult> RenewAsync(
        IUnitOfWork unitOfWork,
        FencedLease lease,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    );

    /// <summary>Settles the attempt inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the settlement.</param>
    /// <param name="lease">The lease to settle.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns><see cref="LeaseSettlementStatus.Settled" /> on success, or why the settlement was refused.</returns>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    ValueTask<LeaseSettlementStatus> SettleAsync(
        IUnitOfWork unitOfWork,
        FencedLease lease,
        CancellationToken cancellationToken = default
    );

    /// <summary>Releases the lease inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the release.</param>
    /// <param name="lease">The lease to release.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns><see cref="LeaseSettlementStatus.Released" /> on success, or why the release was refused.</returns>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    ValueTask<LeaseSettlementStatus> ReleaseAsync(
        IUnitOfWork unitOfWork,
        FencedLease lease,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks inside <paramref name="unitOfWork" />'s transaction that <paramref name="lease" /> is current, active,
    /// and unexpired, and keeps that true until the transaction ends.
    /// </summary>
    /// <param name="unitOfWork">The unit whose writes the fence guards.</param>
    /// <param name="lease">The lease the writes depend on.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>A task that completes when the fence holds.</returns>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the read.</exception>
    /// <exception cref="StaleLeaseException">The attempt no longer owns the lease.</exception>
    ValueTask FenceAsync(IUnitOfWork unitOfWork, FencedLease lease, CancellationToken cancellationToken = default);
}
