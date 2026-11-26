// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Idempotency;

/// <summary>
/// Autonomous idempotency: each call runs the enlisted path in an owned unit the record store begins, so the record
/// change commits before the call returns.
/// </summary>
/// <remarks>
/// A call whose write an optimistic store refuses with <see cref="IdempotencyRecordConflictException" /> runs again,
/// whole, in a fresh owned unit, so it decides from the record as the winning writer left it. A locking store never
/// refuses that way, so its calls run once.
/// </remarks>
internal sealed class IdempotentOperations(
    IUnitOfWorkIdempotency enlisted,
    IIdempotencyRecordStore store,
    IdempotencyRequestResolver resolver
) : IIdempotentOperations
{
    // Each refused attempt means another writer's change to the same record landed, so contention cannot starve every
    // caller at once; the bound only stops a caller that keeps losing to a stream of writers on one key. No delay
    // between attempts: the retry re-reads what the winner wrote rather than waiting for a lock to clear.
    private const int _MaxConflictAttempts = 8;

    public ValueTask<IdempotentAdmission> AdmitAsync(
        string key,
        IdempotencyFingerprint fingerprint,
        string? expectedContract = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default
    )
    {
        return _RetryOnConflictAsync(
            ct => _AdmitOnceAsync(key, fingerprint, expectedContract, leaseDuration, retention, ct),
            cancellationToken
        );
    }

    public async ValueTask CompleteAsync(
        IdempotentAdmission admission,
        ReadOnlyMemory<byte> result,
        string contract,
        TimeSpan? retention = null,
        CancellationToken cancellationToken = default
    )
    {
        await _RetryOnConflictAsync(
                async ct =>
                {
                    await _CompleteOnceAsync(admission, result, contract, retention, ct).ConfigureAwait(false);

                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask SetRecoveryPointAsync(
        IdempotentAdmission admission,
        string point,
        ReadOnlyMemory<byte> state,
        string contract,
        CancellationToken cancellationToken = default
    )
    {
        await _RetryOnConflictAsync(
                async ct =>
                {
                    await _SetRecoveryPointOnceAsync(admission, point, state, contract, ct).ConfigureAwait(false);

                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public ValueTask<IdempotentLeaseStatus> ReleaseAsync(
        IdempotentAdmission admission,
        CancellationToken cancellationToken = default
    )
    {
        return _RetryOnConflictAsync(ct => _ReleaseOnceAsync(admission, ct), cancellationToken);
    }

    private static async ValueTask<T> _RetryOnConflictAsync<T>(
        Func<CancellationToken, ValueTask<T>> call,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await call(cancellationToken).ConfigureAwait(false);
            }
            catch (IdempotencyRecordConflictException) when (attempt < _MaxConflictAttempts)
            {
                // The attempt's owned unit already rolled back and its write was never applied; start over.
            }
        }
    }

    private async ValueTask<IdempotentAdmission> _AdmitOnceAsync(
        string key,
        IdempotencyFingerprint fingerprint,
        string? expectedContract,
        TimeSpan? leaseDuration,
        TimeSpan? retention,
        CancellationToken cancellationToken
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

    private async ValueTask _CompleteOnceAsync(
        IdempotentAdmission admission,
        ReadOnlyMemory<byte> result,
        string contract,
        TimeSpan? retention,
        CancellationToken cancellationToken
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

    private async ValueTask _SetRecoveryPointOnceAsync(
        IdempotentAdmission admission,
        string point,
        ReadOnlyMemory<byte> state,
        string contract,
        CancellationToken cancellationToken
    )
    {
        var unit = await store.BeginOwnedUnitAsync(cancellationToken).ConfigureAwait(false);

        await using (unit.ConfigureAwait(false))
        {
            try
            {
                await enlisted
                    .SetRecoveryPointAsync(unit, admission, point, state, contract, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                await unit.RollbackAsync().ConfigureAwait(false);

                throw;
            }

            // The step it records already happened; a late cancel must not discard the record of it, or a retry
            // would run the step again.
            await unit.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async ValueTask<IdempotentLeaseStatus> _ReleaseOnceAsync(
        IdempotentAdmission admission,
        CancellationToken cancellationToken
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

    public ValueTask<IdempotentResult?> GetResultAsync(string key, CancellationToken cancellationToken = default)
    {
        var recordKey = resolver.ResolvePeek(key);

        return store.GetResultAsync(recordKey, cancellationToken);
    }
}
