// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Idempotency;

/// <summary>
/// Autonomous idempotency: each call runs the enlisted path in an owned unit the record store begins, so the record
/// change commits before the call returns.
/// </summary>
internal sealed class IdempotentOperations(
    IUnitOfWorkIdempotency enlisted,
    IIdempotencyRecordStore store,
    IdempotencyRequestResolver resolver
) : IIdempotentOperations
{
    public async ValueTask<IdempotentAdmission> AdmitAsync(
        string key,
        IdempotencyFingerprint fingerprint,
        string? expectedContract = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default
    )
    {
        var unit = await store.BeginOwnedUnitAsync(cancellationToken).ConfigureAwait(false);

        await using (unit.ConfigureAwait(false))
        {
            IdempotentAdmission admission;

            try
            {
                admission = await enlisted
                    .AdmitAsync(unit, key, fingerprint, expectedContract, leaseDuration, retention, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                await unit.RollbackAsync().ConfigureAwait(false);

                throw;
            }

            if (!admission.IsAdmitted)
            {
                // Nothing this caller wrote is worth keeping: a record it inserted but could not admit would only
                // hold a lock-free placeholder for the attempt that owns the key.
                await unit.RollbackAsync().ConfigureAwait(false);

                return admission;
            }

            // The admission is committed only once, and a late cancel must not strand a granted lease the caller never
            // learns about: until it expires, every retry of the key would see it in flight.
            await unit.CompleteAsync(CancellationToken.None).ConfigureAwait(false);

            return admission;
        }
    }

    public async ValueTask CompleteAsync(
        IdempotentAdmission admission,
        ReadOnlyMemory<byte> result,
        string contract,
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default
    )
    {
        var unit = await store.BeginOwnedUnitAsync(cancellationToken).ConfigureAwait(false);

        await using (unit.ConfigureAwait(false))
        {
            try
            {
                await enlisted
                    .CompleteAsync(unit, admission, result, contract, retention, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                await unit.RollbackAsync().ConfigureAwait(false);

                throw;
            }

            // The result is the attempt's final write; once written, a late cancel must not
            // discard them.
            await unit.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask<IdempotentLeaseStatus> ReleaseAsync(
        IdempotentAdmission admission,
        CancellationToken cancellationToken = default
    )
    {
        var unit = await store.BeginOwnedUnitAsync(cancellationToken).ConfigureAwait(false);

        await using (unit.ConfigureAwait(false))
        {
            IdempotentLeaseStatus status;

            try
            {
                status = await enlisted.ReleaseAsync(unit, admission, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await unit.RollbackAsync().ConfigureAwait(false);

                throw;
            }

            if (status != IdempotentLeaseStatus.Released)
            {
                await unit.RollbackAsync().ConfigureAwait(false);

                return status;
            }

            await unit.CompleteAsync(CancellationToken.None).ConfigureAwait(false);

            return status;
        }
    }

    public ValueTask<IdempotentLeaseRenewal> RenewAsync(
        IdempotentAdmission admission,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        var recordKey = IdempotencyRequestResolver.ResolveAdmitted(admission);
        var lease = resolver.LeaseDuration(duration);

        // One guarded update the store runs and commits on its own connection, with no owned unit around it, which
        // keeps a heartbeat loop cheap.
        return store.RenewAsync(recordKey, admission.Generation!.Value, lease, cancellationToken);
    }

    public ValueTask<IdempotencyPeekStatus> PeekAsync(string key, CancellationToken cancellationToken = default)
    {
        var recordKey = resolver.ResolvePeek(key);

        // No owned unit and no record lock: the store reads the row on its own connection, the same shape as its
        // autonomous purge call.
        return store.PeekAsync(recordKey, cancellationToken);
    }
}
