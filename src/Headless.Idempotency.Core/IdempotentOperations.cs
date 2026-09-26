// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.UnitOfWork;

namespace Headless.Idempotency;

/// <summary>
/// Autonomous idempotency: each call runs the enlisted path in an owned unit the record store begins, so the record
/// change and its fenced-lease change commit together, before the call returns.
/// </summary>
internal sealed class IdempotentOperations(
    IUnitOfWorkIdempotency enlisted,
    IIdempotencyRecordStore store,
    IFencedLeases leases
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

            // The settlement and the result are the attempt's final writes; once written, a late cancel must not
            // discard them.
            await unit.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask<LeaseSettlementStatus> ReleaseAsync(
        IdempotentAdmission admission,
        CancellationToken cancellationToken = default
    )
    {
        var unit = await store.BeginOwnedUnitAsync(cancellationToken).ConfigureAwait(false);

        await using (unit.ConfigureAwait(false))
        {
            LeaseSettlementStatus status;

            try
            {
                status = await enlisted.ReleaseAsync(unit, admission, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await unit.RollbackAsync().ConfigureAwait(false);

                throw;
            }

            if (status != LeaseSettlementStatus.Released)
            {
                await unit.RollbackAsync().ConfigureAwait(false);

                return status;
            }

            await unit.CompleteAsync(CancellationToken.None).ConfigureAwait(false);

            return status;
        }
    }

    public ValueTask<LeaseRenewalResult> RenewAsync(
        IdempotentAdmission admission,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        IdempotencyRequestResolver.ResolveAdmitted(admission);

        // Renewal touches only the lease, never the record, so it needs no record lock and no owned unit: it is one
        // autonomous lease call, which also keeps a heartbeat loop cheap.
        return leases.RenewAsync(admission.Lease!, duration, cancellationToken);
    }
}
