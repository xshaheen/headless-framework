// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Fencing;

/// <summary>
/// Enlisted leases: every verb runs inside the unit (a relational provider on the unit's own connection and
/// transaction), so the unit's outcome decides whether it happened. Every refusal happens before the store runs a
/// command, and nothing here retries.
/// </summary>
internal sealed class UnitOfWorkLeasesFeature(LeaseRequestResolver resolver, ILeaseStore store) : IUnitOfWorkLeases
{
    /// <summary>What an enlisted lease call is called in refusal messages; relational stores reuse it.</summary>
    internal const string Operation = "fenced lease";

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

        _Enlist(unitOfWork, isWrite: true);

        return await store.GrantEnlistedAsync(unitOfWork, key, duration, cancellationToken).ConfigureAwait(false);
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

        _Enlist(unitOfWork, isWrite: true);

        return await store
            .RenewEnlistedAsync(unitOfWork, key, lease.Generation, duration, cancellationToken)
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

        _Enlist(unitOfWork, isWrite: true);

        return await store
            .SettleEnlistedAsync(unitOfWork, key, lease.Generation, cancellationToken)
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

        _Enlist(unitOfWork, isWrite: true);

        return await store
            .ReleaseEnlistedAsync(unitOfWork, key, lease.Generation, cancellationToken)
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

        _Enlist(unitOfWork, isWrite: false);

        var status = await store
            .FenceEnlistedAsync(unitOfWork, key, lease.Generation, cancellationToken)
            .ConfigureAwait(false);

        if (status != LeaseFenceStatus.Current)
        {
            throw new StaleLeaseException(lease, status);
        }
    }

    private void _Enlist(IUnitOfWork unitOfWork, bool isWrite)
    {
        if (unitOfWork.State != UnitOfWorkState.Active)
        {
            throw new InvalidOperationException(
                $"The unit of work is {unitOfWork.State}; a {Operation} needs a live transaction to join."
            );
        }

        // What the unit must carry is the provider's to judge: a relational store needs a live transaction on its own
        // database, while an in-process store refuses one, because its state cannot commit atomically with it.
        store.ValidateEnlistment(unitOfWork);

        // A lease write is not tracked by the unit's change tracker, so a replay cannot restore it; it has to be
        // re-run. An owned unit replays the caller's block, which re-runs the write, so it stays replayable. An
        // observed unit belongs to someone else's commit edge (the EF save pipeline's own save), whose replay would
        // not re-run a grant or settlement the rolled-back transaction discarded. A resource-less unit has no
        // execution strategy that could replay it. The fence only reads, so re-running it is always safe. Marked only
        // after every check passed, so a refused call never makes the unit non-retryable.
        if (isWrite && unitOfWork.Resource is { IsOwned: false })
        {
            unitOfWork.PreventRetry();
        }
    }
}
