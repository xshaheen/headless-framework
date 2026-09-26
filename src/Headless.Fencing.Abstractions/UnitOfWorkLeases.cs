// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Fencing;

/// <summary>
/// One unit-of-work handle bound to fenced leases, returned by <c>unit.Leases</c>. Every call runs inside that unit's
/// transaction, so it takes effect only when the unit commits.
/// </summary>
/// <remarks>
/// One binding per unit, created on the first read of <c>unit.Leases</c> and kept as unit-local state; it owns nothing
/// to dispose. The unit's liveness is checked on each call, so a binding kept past the unit's completion throws on its
/// next call.
/// </remarks>
[PublicAPI]
public sealed class UnitOfWorkLeases
{
    private readonly IUnitOfWorkLeases _leases;
    private readonly IUnitOfWork _unitOfWork;

    internal UnitOfWorkLeases(IUnitOfWorkLeases leases, IUnitOfWork unitOfWork)
    {
        _leases = leases;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Grants the lease inside the bound unit's transaction.</summary>
    /// <remarks>
    /// The lease row stays locked until the unit ends, and every other grant, renewal, settlement, or sweep of the
    /// lease waits for it; a grant the unit rolls back never happened.
    /// </remarks>
    /// <param name="kind">The lease kind.</param>
    /// <param name="resource">The leased resource within the kind.</param>
    /// <param name="duration">How long the lease lives unless renewed; bounded by the configured limits.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The grant's result.</returns>
    /// <exception cref="ArgumentException">The kind, resource, or current tenant id is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is outside the configured bounds.</exception>
    /// <exception cref="InvalidOperationException">The bound unit is no longer active or cannot host the write.</exception>
    public ValueTask<LeaseGrantResult> GrantAsync(
        string kind,
        string resource,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        return _leases.GrantAsync(_unitOfWork, kind, resource, duration, cancellationToken);
    }

    /// <summary>Renews the lease inside the bound unit's transaction.</summary>
    /// <param name="lease">The lease to renew.</param>
    /// <param name="duration">The new time to live; bounded by the configured limits.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The renewal's result.</returns>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is outside the configured bounds.</exception>
    /// <exception cref="InvalidOperationException">The bound unit is no longer active or cannot host the write.</exception>
    public ValueTask<LeaseRenewalResult> RenewAsync(
        FencedLease lease,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        return _leases.RenewAsync(_unitOfWork, lease, duration, cancellationToken);
    }

    /// <summary>
    /// Settles the attempt inside the bound unit's transaction, so the settlement commits together with the
    /// attempt's result.
    /// </summary>
    /// <param name="lease">The lease to settle.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns><see cref="LeaseSettlementStatus.Settled" /> on success, or why the settlement was refused.</returns>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    /// <exception cref="InvalidOperationException">The bound unit is no longer active or cannot host the write.</exception>
    public ValueTask<LeaseSettlementStatus> SettleAsync(
        FencedLease lease,
        CancellationToken cancellationToken = default
    )
    {
        return _leases.SettleAsync(_unitOfWork, lease, cancellationToken);
    }

    /// <summary>Releases the lease inside the bound unit's transaction.</summary>
    /// <param name="lease">The lease to release.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns><see cref="LeaseSettlementStatus.Released" /> on success, or why the release was refused.</returns>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    /// <exception cref="InvalidOperationException">The bound unit is no longer active or cannot host the write.</exception>
    public ValueTask<LeaseSettlementStatus> ReleaseAsync(
        FencedLease lease,
        CancellationToken cancellationToken = default
    )
    {
        return _leases.ReleaseAsync(_unitOfWork, lease, cancellationToken);
    }

    /// <summary>
    /// Refuses the bound unit's writes unless <paramref name="lease" /> is current, active, and unexpired, and keeps
    /// that answer true until the unit commits.
    /// </summary>
    /// <remarks>
    /// Call it before the writes it guards. It locks the lease row for update until the unit ends, so a concurrent
    /// grant, renewal, or sweep of the lease waits instead of replacing the generation under the unit. Safe to repeat,
    /// and never makes the unit non-retryable.
    /// </remarks>
    /// <param name="lease">The lease the writes depend on.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>A task that completes when the fence holds.</returns>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    /// <exception cref="InvalidOperationException">The bound unit is no longer active or cannot host the read.</exception>
    /// <exception cref="StaleLeaseException">The attempt no longer owns the lease.</exception>
    public ValueTask FenceAsync(FencedLease lease, CancellationToken cancellationToken = default)
    {
        return _leases.FenceAsync(_unitOfWork, lease, cancellationToken);
    }
}
