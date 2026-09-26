// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Fencing;

/// <summary>
/// Enlisted leases: every verb runs on the unit's own connection and transaction, so the unit's outcome decides
/// whether it happened. Every refusal happens before the store runs a command, and nothing here retries.
/// </summary>
internal sealed class UnitOfWorkLeasesFeature(LeaseRequestResolver resolver, ILeaseStore store) : IUnitOfWorkLeases
{
    private const string _Operation = "fenced lease";

    public async ValueTask<LeaseGrantResult> GrantAsync(
        IUnitOfWork unitOfWork,
        string kind,
        string resource,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var key = resolver.Resolve(kind, resource);
        resolver.ValidateDuration(duration);

        var relational = _Enlist(unitOfWork, isWrite: true);

        return await store.GrantEnlistedAsync(relational, key, duration, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<LeaseRenewalResult> RenewAsync(
        IUnitOfWork unitOfWork,
        FencedLease lease,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var key = LeaseRequestResolver.ResolveLease(lease);
        resolver.ValidateDuration(duration);

        var relational = _Enlist(unitOfWork, isWrite: true);

        return await store
            .RenewEnlistedAsync(relational, key, lease.Generation, duration, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<LeaseSettlementStatus> SettleAsync(
        IUnitOfWork unitOfWork,
        FencedLease lease,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var key = LeaseRequestResolver.ResolveLease(lease);

        var relational = _Enlist(unitOfWork, isWrite: true);

        return await store
            .SettleEnlistedAsync(relational, key, lease.Generation, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<LeaseSettlementStatus> ReleaseAsync(
        IUnitOfWork unitOfWork,
        FencedLease lease,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var key = LeaseRequestResolver.ResolveLease(lease);

        var relational = _Enlist(unitOfWork, isWrite: true);

        return await store
            .ReleaseEnlistedAsync(relational, key, lease.Generation, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask FenceAsync(
        IUnitOfWork unitOfWork,
        FencedLease lease,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var key = LeaseRequestResolver.ResolveLease(lease);

        var relational = _Enlist(unitOfWork, isWrite: false);

        var status = await store
            .FenceEnlistedAsync(relational, key, lease.Generation, cancellationToken)
            .ConfigureAwait(false);

        if (status != LeaseFenceStatus.Current)
        {
            throw new StaleLeaseException(lease, status);
        }
    }

    private IRelationalUnitOfWorkResource _Enlist(IUnitOfWork unitOfWork, bool isWrite)
    {
        // Checks the unit's state, resource kind, and transaction liveness; the provider-specific transaction type is
        // the store's to judge, so any DbTransaction passes here.
        UnitOfWorkTransactions.RequireTransaction<DbTransaction>(unitOfWork, _Operation);
        var relational = (IRelationalUnitOfWorkResource)unitOfWork.Resource!;

        store.ValidateEnlistment(relational);

        // A lease write is not tracked by the unit's change tracker, so a replay cannot restore it; it has to be
        // re-run. An owned unit replays the caller's block, which re-runs the write, so it stays replayable. An
        // observed unit belongs to someone else's commit edge (the EF save pipeline's own save), whose replay would
        // not re-run a grant or settlement the rolled-back transaction discarded. The fence only reads, so re-running
        // it is always safe. Marked only after every check passed, so a refused call never makes the unit
        // non-retryable.
        if (isWrite && !relational.IsOwned)
        {
            unitOfWork.PreventRetry();
        }

        return relational;
    }
}
