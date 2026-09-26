// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Idempotency;

/// <summary>
/// Enlisted idempotency: every call runs inside the unit (a relational provider on the unit's own connection and
/// transaction), so the unit's outcome decides whether it happened. The key's record carries its own lease, so every call locks exactly one row and decides from
/// that row as the database clock sees it after the lock is held. Every refusal of the arguments or the unit happens
/// before the store runs a command, and nothing here retries.
/// </summary>
internal sealed class UnitOfWorkIdempotencyFeature(IdempotencyRequestResolver resolver, IIdempotencyRecordStore store)
    : IUnitOfWorkIdempotency
{
    /// <summary>What an enlisted idempotency call is called in refusal messages; relational stores reuse it.</summary>
    internal const string Operation = "durable idempotency call";

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
        _Enlist(unitOfWork, isWrite: true);

        var record = await store
            .LockOrInsertAsync(unitOfWork, recordKey, fingerprint, keep, cancellationToken)
            .ConfigureAwait(false);

        var publicKey = recordKey.ToKey();

        // Past retention the stored outcome no longer binds the key and the record is reused as if new, whatever
        // fingerprint or result it held. A live attempt keeps it bound even then: its lease may have been renewed past
        // the retention, and resetting the record under it would hand the key to a second attempt.
        if (!record.Inserted && (!record.IsRetentionElapsed || record.IsHeld))
        {
            if (!fingerprint.Matches(record.Fingerprint))
            {
                return IdempotentAdmission.FingerprintConflict(publicKey, fingerprint, record.Fingerprint);
            }

            if (record.Status == IdempotencyRecordStatus.Completed)
            {
                return _Replay(publicKey, fingerprint, record, expectedContract);
            }

            if (record.IsHeld)
            {
                return IdempotentAdmission.InFlight(
                    publicKey,
                    fingerprint,
                    record.Generation!.Value,
                    record.LeaseExpiresAt!.Value
                );
            }
        }

        // A pending record that still names an attempt means that attempt ended without completing or releasing (it
        // crashed or outlived its lease), so its partial side effects may exist. A released or just-inserted record
        // names none.
        var isTakeover = record is { Inserted: false, Status: IdempotencyRecordStatus.Pending, Generation: not null };

        var grant = await store
            .AdmitAsync(unitOfWork, recordKey, fingerprint, lease, keep, cancellationToken)
            .ConfigureAwait(false);

        return IdempotentAdmission.Admitted(
            publicKey,
            fingerprint,
            grant.Generation,
            grant.LeaseExpiresAt,
            isTakeover,
            keep
        );
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
        var generation = admission.Generation!.Value;

        _Enlist(unitOfWork, isWrite: true);

        // Owning the key is the attempt's last proof that its result is the outcome: an expired lease may already be
        // taken over, and a completed attempt already stored the result a retry must not overwrite.
        await _LockOwnRecordAsync(unitOfWork, recordKey, admission, cancellationToken).ConfigureAwait(false);

        await store
            .CompleteAsync(unitOfWork, recordKey, generation, result, contract, keep, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<IdempotentLeaseStatus> ReleaseAsync(
        IUnitOfWork unitOfWork,
        IdempotentAdmission admission,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var recordKey = IdempotencyRequestResolver.ResolveAdmitted(admission);
        var generation = admission.Generation!.Value;

        _Enlist(unitOfWork, isWrite: true);

        var record = await store.LockAsync(unitOfWork, recordKey, cancellationToken).ConfigureAwait(false);
        var status = record?.ClassifyFor(generation) ?? IdempotentLeaseStatus.Stale;

        if (status != IdempotentLeaseStatus.Current)
        {
            // A released record reports success again, so a retried release that already committed is not an error;
            // every other refusal writes nothing.
            return status;
        }

        await store
            .ReleaseAsync(unitOfWork, recordKey, generation, admission.Retention!.Value, cancellationToken)
            .ConfigureAwait(false);

        return IdempotentLeaseStatus.Released;
    }

    public async ValueTask FenceAsync(
        IUnitOfWork unitOfWork,
        IdempotentAdmission admission,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        var recordKey = IdempotencyRequestResolver.ResolveAdmitted(admission);

        _Enlist(unitOfWork, isWrite: false);

        await _LockOwnRecordAsync(unitOfWork, recordKey, admission, cancellationToken).ConfigureAwait(false);
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
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey recordKey,
        IdempotentAdmission admission,
        CancellationToken cancellationToken
    )
    {
        var generation = admission.Generation!.Value;
        var record = await store.LockAsync(unitOfWork, recordKey, cancellationToken).ConfigureAwait(false);

        // The update-intent lock taken here lasts until the transaction ends, so the answer stays true for every write
        // the unit makes after it: no admission can take the key over until this unit commits or rolls back.
        var status = record?.ClassifyFor(generation) ?? IdempotentLeaseStatus.Stale;

        if (status != IdempotentLeaseStatus.Current)
        {
            throw new StaleAdmissionException(admission.Key, generation, status);
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
        // database, while an in-process store refuses one, because its records cannot commit atomically with it.
        store.ValidateEnlistment(unitOfWork);

        // A record write is not tracked by the unit's change tracker, so a replay cannot restore it; it has to be
        // re-run. An owned unit replays the caller's block, which re-runs the write, so it stays replayable. An
        // observed unit belongs to someone else's commit edge (the EF save pipeline's own save), whose replay would
        // not re-run an admission or completion the rolled-back transaction discarded. A resource-less unit has no
        // execution strategy that could replay it. The fence only locks, so re-running it is always safe. Marked only
        // after every check passed, so a refused call never makes the unit non-retryable.
        if (isWrite && unitOfWork.Resource is { IsOwned: false })
        {
            unitOfWork.PreventRetry();
        }
    }
}
