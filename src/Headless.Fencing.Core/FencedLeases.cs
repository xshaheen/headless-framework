// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Fencing;

/// <summary>
/// Autonomous leases: each verb is one committed call on the provider's own connection, and the sweep runs each claim
/// and its handler in an owned unit the store begins.
/// </summary>
internal sealed class FencedLeases(LeaseRequestResolver resolver, ILeaseStore store) : IFencedLeases
{
    public async ValueTask<LeaseGrantResult> GrantAsync(
        string kind,
        string resource,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        var key = resolver.Resolve(kind, resource);
        resolver.ValidateDuration(duration);

        return await store.GrantAsync(key, duration, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<LeaseRenewalResult> RenewAsync(
        FencedLease lease,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        var key = LeaseRequestResolver.ResolveLease(lease);
        resolver.ValidateDuration(duration);

        return await store.RenewAsync(key, lease.Generation, duration, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<LeaseSettlementStatus> SettleAsync(
        FencedLease lease,
        CancellationToken cancellationToken = default
    )
    {
        var key = LeaseRequestResolver.ResolveLease(lease);

        return await store.SettleAsync(key, lease.Generation, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<LeaseSettlementStatus> ReleaseAsync(
        FencedLease lease,
        CancellationToken cancellationToken = default
    )
    {
        var key = LeaseRequestResolver.ResolveLease(lease);

        return await store.ReleaseAsync(key, lease.Generation, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<LeaseSweepResult> SweepExpiredAsync(
        string kind,
        Func<ExpiredLease, IUnitOfWork, CancellationToken, ValueTask> handler,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        var storedKind = LeaseRequestResolver.ResolveKind(kind);
        Argument.IsNotNull(handler);
        Argument.IsPositive(limit);

        var handled = new List<ExpiredLease>();
        var failures = new List<LeaseSweepFailure>();

        // The cursor moves past every visited lease, committed or not, so a lease whose handler threw is left for a
        // later call instead of being re-claimed in a loop by this one.
        ExpiredLease? cursor = null;

        while (handled.Count + failures.Count < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var claimed = await _SweepOneAsync(storedKind, cursor, handler, cancellationToken).ConfigureAwait(false);

            if (claimed is null)
            {
                break;
            }

            cursor = claimed.Value.Lease;

            if (claimed.Value.Failure is null)
            {
                handled.Add(claimed.Value.Lease);
            }
            else
            {
                failures.Add(claimed.Value.Failure);
            }
        }

        return new LeaseSweepResult(handled, failures);
    }

    public async ValueTask<int> PurgeAsync(
        string kind,
        TimeSpan olderThan,
        CancellationToken cancellationToken = default
    )
    {
        var storedKind = LeaseRequestResolver.ResolveKind(kind);
        Argument.IsPositiveOrZero(olderThan);

        return await store.PurgeAsync(storedKind, olderThan, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<(ExpiredLease Lease, LeaseSweepFailure? Failure)?> _SweepOneAsync(
        string kind,
        ExpiredLease? cursor,
        Func<ExpiredLease, IUnitOfWork, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken
    )
    {
        var unit = await store.BeginOwnedUnitAsync(cancellationToken).ConfigureAwait(false);

        await using (unit.ConfigureAwait(false))
        {
            // The store began the unit itself, so it needs no enlistment check before the claim runs in it.
            var lease = await store
                .ClaimExpiredEnlistedAsync(unit, kind, cursor, cancellationToken)
                .ConfigureAwait(false);

            if (lease is null)
            {
                await unit.RollbackAsync().ConfigureAwait(false);

                return null;
            }

            try
            {
                await handler(lease, unit, cancellationToken).ConfigureAwait(false);

                // The claim and the handoff are the lease's final writes; once the handler has run, a late cancel
                // must not discard them.
                await unit.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await unit.RollbackAsync().ConfigureAwait(false);

                throw;
            }
#pragma warning disable CA1031 // The handler is caller code at a per-lease boundary: its failure is recorded in the result, and only its own lease rolls back.
            catch (Exception e)
#pragma warning restore CA1031
            {
                await unit.RollbackAsync().ConfigureAwait(false);

                return (lease, new LeaseSweepFailure(lease, e));
            }

            return (lease, null);
        }
    }
}
