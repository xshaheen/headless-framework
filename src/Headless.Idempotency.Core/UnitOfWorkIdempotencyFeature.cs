// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Fencing;
using Headless.UnitOfWork;

namespace Headless.Idempotency;

/// <summary>
/// Enlisted idempotency: every call runs on the unit's own connection and transaction, so the unit's outcome decides
/// whether it happened. Every call locks the key's record before it touches the key's fenced lease, the one order
/// that keeps a fence, a completion, and a concurrent admission of the same key from deadlocking. Every refusal of the
/// arguments or the unit happens before the store runs a command, and nothing here retries.
/// </summary>
internal sealed class UnitOfWorkIdempotencyFeature(
    IdempotencyRequestResolver resolver,
    IIdempotencyRecordStore store,
    IUnitOfWorkLeases leases
) : IUnitOfWorkIdempotency
{
    private const string _Operation = "durable idempotency call";

    public async ValueTask<IdempotentAdmission> AdmitAsync(
        IUnitOfWork unitOfWork,
        string key,
        IdempotencyFingerprint fingerprint,
        string? expectedContract = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var recordKey = resolver.ResolveAdmission(key, fingerprint, expectedContract);
        var lease = resolver.LeaseDuration(leaseDuration);
        var keep = resolver.Retention(retention);

        // Admission may insert the record even when it ends up replaying, so it is always a write.
        var relational = _Enlist(unitOfWork, isWrite: true);

        var record = await store
            .LockOrInsertAsync(relational, recordKey, fingerprint, keep, cancellationToken)
            .ConfigureAwait(false);

        var publicKey = recordKey.ToKey();

        // Past retention the stored outcome no longer binds the key: the record is reused as if new, whatever
        // fingerprint or result it held.
        if (!record.Inserted && !record.IsRetentionElapsed)
        {
            if (!fingerprint.Matches(record.Fingerprint))
            {
                return IdempotentAdmission.FingerprintConflict(publicKey, fingerprint, record.Fingerprint);
            }

            if (record.Status == IdempotencyRecordStatus.Completed)
            {
                return _Replay(publicKey, fingerprint, record, expectedContract);
            }
        }

        // A pending record that still names an attempt means that attempt ended without completing or releasing (it
        // crashed, outlived its lease, or a sweep abandoned it), so its partial side effects may exist. A released or
        // just-inserted record names none.
        var isTakeover =
            record is { Inserted: false, Status: IdempotencyRecordStatus.Pending, LeaseGeneration: not null };

        var grant = await leases
            .GrantAsync(unitOfWork, IdempotentAdmission.LeaseKind, recordKey.Key, lease, cancellationToken)
            .ConfigureAwait(false);

        if (!grant.IsAcquired)
        {
            return IdempotentAdmission.InFlight(publicKey, fingerprint, grant.ExpiresAt);
        }

        await store
            .AdmitAsync(relational, recordKey, fingerprint, grant.Lease.Generation, keep, cancellationToken)
            .ConfigureAwait(false);

        return IdempotentAdmission.Admitted(publicKey, fingerprint, grant.Lease, grant.ExpiresAt, isTakeover, keep);
    }

    public async ValueTask CompleteAsync(
        IUnitOfWork unitOfWork,
        IdempotentAdmission admission,
        ReadOnlyMemory<byte> result,
        string contract,
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var recordKey = IdempotencyRequestResolver.ResolveAdmitted(admission);
        IdempotencyRequestResolver.ValidateContract(contract, nameof(contract));
        var keep = retention is null ? admission.Retention!.Value : resolver.Retention(retention);
        var lease = admission.Lease!;

        var relational = _Enlist(unitOfWork, isWrite: true);

        await _LockOwnRecordAsync(relational, recordKey, lease, cancellationToken).ConfigureAwait(false);

        var settlement = await leases.SettleAsync(unitOfWork, lease, cancellationToken).ConfigureAwait(false);

        // Settling is the attempt's last proof that it still owns the key; without it, a takeover's attempt owns the
        // outcome, and writing this result would overwrite or pre-empt that attempt's.
        if (settlement != LeaseSettlementStatus.Settled)
        {
            throw new StaleLeaseException(lease, _ToFenceStatus(settlement));
        }

        await store
            .CompleteAsync(relational, recordKey, lease.Generation, result, contract, keep, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<LeaseSettlementStatus> ReleaseAsync(
        IUnitOfWork unitOfWork,
        IdempotentAdmission admission,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var recordKey = IdempotencyRequestResolver.ResolveAdmitted(admission);
        var lease = admission.Lease!;

        var relational = _Enlist(unitOfWork, isWrite: true);

        var record = await store.LockAsync(relational, recordKey, cancellationToken).ConfigureAwait(false);

        if (record?.LeaseGeneration != lease.Generation)
        {
            // Another attempt owns the record now; releasing this generation's lease could not free it anyway.
            return LeaseSettlementStatus.Stale;
        }

        var release = await leases.ReleaseAsync(unitOfWork, lease, cancellationToken).ConfigureAwait(false);

        if (release == LeaseSettlementStatus.Released)
        {
            await store
                .ReleaseAsync(relational, recordKey, lease.Generation, admission.Retention!.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        return release;
    }

    public async ValueTask FenceAsync(
        IUnitOfWork unitOfWork,
        IdempotentAdmission admission,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var recordKey = IdempotencyRequestResolver.ResolveAdmitted(admission);
        var lease = admission.Lease!;

        var relational = _Enlist(unitOfWork, isWrite: false);

        await _LockOwnRecordAsync(relational, recordKey, lease, cancellationToken).ConfigureAwait(false);
        await leases.FenceAsync(unitOfWork, lease, cancellationToken).ConfigureAwait(false);
    }

    private static IdempotentAdmission _Replay(
        IdempotencyKey key,
        IdempotencyFingerprint fingerprint,
        IdempotencyRecordState record,
        string? expectedContract
    )
    {
        var stored =
            record.Result
            ?? throw new InvalidOperationException(
                $"The idempotency record for key '{key.Key}' is completed but carries no result; the record store "
                    + "returned an inconsistent row."
            );

        // Bytes written under another contract would be misread by this caller, so they are refused, not replayed.
        if (expectedContract is not null && !string.Equals(stored.Contract, expectedContract, StringComparison.Ordinal))
        {
            return IdempotentAdmission.ContractConflict(key, fingerprint, stored.Contract);
        }

        return IdempotentAdmission.Replay(key, fingerprint, stored);
    }

    private async ValueTask _LockOwnRecordAsync(
        IRelationalUnitOfWorkResource relational,
        IdempotencyRecordKey recordKey,
        FencedLease lease,
        CancellationToken cancellationToken
    )
    {
        var record = await store.LockAsync(relational, recordKey, cancellationToken).ConfigureAwait(false);

        // The record names the attempt that owns it. A record gone, released, or re-admitted under another generation
        // belongs to no attempt holding this lease, so the lease is not even consulted.
        if (record?.LeaseGeneration != lease.Generation)
        {
            throw new StaleLeaseException(lease, LeaseFenceStatus.Stale);
        }
    }

    private static LeaseFenceStatus _ToFenceStatus(LeaseSettlementStatus settlement)
    {
        return settlement switch
        {
            LeaseSettlementStatus.Expired => LeaseFenceStatus.Expired,
            LeaseSettlementStatus.Released => LeaseFenceStatus.Released,
            LeaseSettlementStatus.Abandoned => LeaseFenceStatus.Abandoned,
            LeaseSettlementStatus.Settled => LeaseFenceStatus.Settled,
            _ => LeaseFenceStatus.Stale,
        };
    }

    private IRelationalUnitOfWorkResource _Enlist(IUnitOfWork unitOfWork, bool isWrite)
    {
        // Checks the unit's state, resource kind, and transaction liveness; the provider-specific transaction type is
        // the store's to judge, so any DbTransaction passes here.
        UnitOfWorkTransactions.RequireTransaction<DbTransaction>(unitOfWork, _Operation);
        var relational = (IRelationalUnitOfWorkResource)unitOfWork.Resource!;

        store.ValidateEnlistment(relational);

        // A record write is not tracked by the unit's change tracker, so a replay cannot restore it; it has to be
        // re-run. An owned unit replays the caller's block, which re-runs the write, so it stays replayable. An
        // observed unit belongs to someone else's commit edge (the EF save pipeline's own save), whose replay would
        // not re-run an admission or completion the rolled-back transaction discarded. The fence only locks, so
        // re-running it is always safe. Marked only after every check passed, so a refused call never makes the unit
        // non-retryable.
        if (isWrite && !relational.IsOwned)
        {
            unitOfWork.PreventRetry();
        }

        return relational;
    }
}
