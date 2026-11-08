// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Fencing;
using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;

namespace Headless.Idempotency.InMemory;

/// <summary>
/// The in-memory record store: one process-local table of records, one exclusive lock per record key, and one
/// store-wide generation counter. Each record's lease moves through <see cref="RowLeaseRules" />, the same transitions
/// the relational providers run in SQL, so both answer a verb the same way. Every verb that decides takes its key's lock and only then reads the injected
/// <see cref="TimeProvider" />, so a decision is never made on a clock read from before a lock wait. Inside one unit
/// the reading never runs backwards, so the instants one admission writes across its insert and its grant agree even
/// when the wall clock steps back between them.
/// </summary>
/// <remarks>
/// <para>
/// The injected clock is the authority here, the single-process counterpart of the relational providers' database
/// clock. State lives in this singleton and disappears with the process, so it coordinates the callers of one process
/// only.
/// </para>
/// <para>
/// Every enlisted verb runs on a unit that carries no relational resource: the unit itself is the commit boundary. The
/// record's lock is held until the unit ends, the unit's writes reach the table only when it completes, and a rollback
/// or an abandoned unit drops them. A unit whose work commits in a database is refused, because records in this
/// process cannot commit or roll back with that transaction.
/// </para>
/// </remarks>
internal sealed class InMemoryIdempotencyRecordStore(
    InMemoryIdempotencyStorage storage,
    IUnitOfWorkFactory unitOfWorkFactory,
    TimeProvider timeProvider
) : IIdempotencyRecordStore
{
    // No holder: an admission is granted only to a record no live attempt holds, so no retried grant needs matching.
    private static readonly LeaseGrantOptions _AdmitOptions = new();

    private InMemoryRowTable<IdempotencyRecordKey, InMemoryIdempotencyRecord> Table => storage.Table;

    #region Owned units and enlistment

    public ValueTask<IUnitOfWork> BeginOwnedUnitAsync(CancellationToken cancellationToken = default)
    {
        return unitOfWorkFactory.BeginAsync(cancellationToken: cancellationToken);
    }

    public void ValidateEnlistment(IUnitOfWork unitOfWork)
    {
        Argument.IsNotNull(unitOfWork);

        if (unitOfWork.Resource is IRelationalUnitOfWorkResource)
        {
            throw new InvalidOperationException(
                "The unit of work runs on a database transaction, but Headless.Idempotency.InMemory keeps records "
                    + "in process memory, which cannot commit or roll back with it. Call enlisted idempotency methods "
                    + "on a resource-less unit (IUnitOfWorkFactory.BeginAsync()), or use IIdempotentOperations for "
                    + "autonomous calls."
            );
        }
    }

    #endregion

    #region Lock

    public async ValueTask<IdempotencyRecordState> LockOrInsertAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(fingerprint);
        var transaction = await _LockAsync(unitOfWork, key, cancellationToken).ConfigureAwait(false);
        var now = transaction.Now(timeProvider);

        if (transaction.Read(key) is { } existing)
        {
            return _State(existing, now, inserted: false);
        }

        var inserted = new InMemoryIdempotencyRecord(Lease: default, fingerprint, Result: null, _Add(now, retention));
        transaction.Stage(key, inserted);

        return _State(inserted, now, inserted: true);
    }

    public async ValueTask<IdempotencyRecordState?> LockAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        CancellationToken cancellationToken = default
    )
    {
        var transaction = await _LockAsync(unitOfWork, key, cancellationToken).ConfigureAwait(false);

        return transaction.Read(key) is { } record
            ? _State(record, transaction.Now(timeProvider), inserted: false)
            : null;
    }

    #endregion

    #region Admit, complete, release

    public async ValueTask<IdempotencyRecordGrant> AdmitAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        TimeSpan leaseDuration,
        TimeSpan retention,
        bool keepRecoveryPoint,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(fingerprint);
        var transaction = await _LockAsync(unitOfWork, key, cancellationToken).ConfigureAwait(false);
        var record = transaction.Read(key) ?? throw _NotWritten(key, "admit");
        var now = transaction.Now(timeProvider);

        // Drawn only now, under the record's lock: a generation drawn before the lock could be lower than one a
        // still-open admission already holds. Drawn from the store-wide counter rather than the record's own count,
        // because a purged key comes back and an old token must never match the new record's generations.
        var grant = RowLeaseRules.Grant(record.Lease, now, leaseDuration, _AdmitOptions, storage.NextGeneration());

        if (!grant.Wrote)
        {
            throw new InvalidOperationException(
                $"Could not admit an attempt on the idempotency record '{key.Key}': it was held by a live attempt "
                    + "inside the unit that locked it and found it free."
            );
        }

        // Also the in-place reset of a record past its retention: every outcome field is overwritten.
        transaction.Stage(
            key,
            new InMemoryIdempotencyRecord(
                grant.Next,
                fingerprint,
                Result: null,
                _Extend(record.RetentionUntil, now, retention),
                keepRecoveryPoint ? record.RecoveryPoint : null
            )
        );

        return new IdempotencyRecordGrant(grant.Next.Generation, grant.Next.ExpiresAt!.Value);
    }

    public async ValueTask CompleteAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        long generation,
        ReadOnlyMemory<byte> result,
        string contract,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(contract);
        var transaction = await _LockAsync(unitOfWork, key, cancellationToken).ConfigureAwait(false);
        var record = _Require(transaction.Read(key), key, "complete");
        var now = transaction.Now(timeProvider);
        var settled = RowLeaseRules.Settle(record.Lease, generation, now);
        _RequireEnded(settled.Status, LeaseSettlementStatus.Settled, key, "complete");

        // A repeat by the generation that already settled writes nothing, so the stored result is never overwritten.
        if (!settled.Wrote)
        {
            return;
        }

        // The result copies the caller's bytes.
        transaction.Stage(
            key,
            record with
            {
                Lease = settled.Next,
                Result = new IdempotentResult(result.Span, contract),
                RetentionUntil = _Extend(record.RetentionUntil, now, retention),
                RecoveryPoint = null,
            }
        );
    }

    public async ValueTask SetRecoveryPointAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        long generation,
        string point,
        ReadOnlyMemory<byte> state,
        string contract,
        CancellationToken cancellationToken = default
    )
    {
        var transaction = await _LockAsync(unitOfWork, key, cancellationToken).ConfigureAwait(false);
        var record = _Require(transaction.Read(key), key, "set the recovery point of");
        var status = RowLeaseRules.Judge(record.Lease, generation, transaction.Now(timeProvider));

        if (status != LeaseFenceStatus.Current)
        {
            throw _NotOwned(key, "set the recovery point of", status.ToString());
        }

        // Staged like every other write, so the point reaches the table only when the unit completes and a rollback
        // drops it with the step's own work. The point copies the caller's bytes.
        transaction.Stage(
            key,
            record with
            {
                RecoveryPoint = new IdempotentRecoveryPoint(point, state.Span, contract),
            }
        );
    }

    public async ValueTask ReleaseAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        long generation,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        var transaction = await _LockAsync(unitOfWork, key, cancellationToken).ConfigureAwait(false);
        var record = _Require(transaction.Read(key), key, "release");
        var now = transaction.Now(timeProvider);
        var released = RowLeaseRules.Release(record.Lease, generation, now);
        _RequireEnded(released.Status, LeaseSettlementStatus.Released, key, "release");

        if (!released.Wrote)
        {
            return;
        }

        transaction.Stage(
            key,
            record with
            {
                Lease = released.Next,
                RetentionUntil = _Extend(record.RetentionUntil, now, retention),
            }
        );
    }

    #endregion

    #region Renew and peek

    public async ValueTask<IdempotentLeaseRenewal> RenewAsync(
        IdempotencyRecordKey key,
        long generation,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        using var held = await Table.LockAsync(key, cancellationToken).ConfigureAwait(false);

        if (Table.Read(key) is not { } record)
        {
            return new IdempotentLeaseRenewal(IdempotentLeaseStatus.Stale, ExpiresAt: null);
        }

        var renewed = RowLeaseRules.Renew(record.Lease, generation, timeProvider.GetUtcNow(), leaseDuration);

        if (renewed.Wrote)
        {
            Table.Write(key, record with { Lease = renewed.Next });
        }

        // The relational store maps the same statuses: an older generation is stale even when a newer attempt has
        // since released the record, because only the generation that ended it can see how it ended.
        return renewed.Status switch
        {
            LeaseRenewalStatus.Renewed => new(IdempotentLeaseStatus.Current, renewed.Next.ExpiresAt),
            LeaseRenewalStatus.Expired => new(IdempotentLeaseStatus.Expired, renewed.Next.ExpiresAt),
            LeaseRenewalStatus.Settled => new(IdempotentLeaseStatus.Completed, ExpiresAt: null),
            LeaseRenewalStatus.Released => new(IdempotentLeaseStatus.Released, ExpiresAt: null),
            _ => new(IdempotentLeaseStatus.Stale, ExpiresAt: null),
        };
    }

    public ValueTask<IdempotencyPeekStatus> PeekAsync(
        IdempotencyRecordKey key,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // No lock: the committed row is an immutable snapshot, so this never waits behind a unit that holds the key,
        // and a unit's uncommitted writes are invisible to it.
        var now = timeProvider.GetUtcNow();

        if (Table.Read(key) is not { } record || record.RetentionUntil <= now)
        {
            return ValueTask.FromResult(IdempotencyPeekStatus.Absent);
        }

        if (record.Lease.State == RowLeaseState.Settled)
        {
            return ValueTask.FromResult(IdempotencyPeekStatus.Completed);
        }

        return ValueTask.FromResult(
            _IsLive(record.Lease, now) ? IdempotencyPeekStatus.Pending : IdempotencyPeekStatus.Released
        );
    }

    public ValueTask<IdempotentResult?> GetResultAsync(
        IdempotencyRecordKey key,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The same lock-free committed snapshot PeekAsync reads.
        var result =
            Table.Read(key) is { Lease.State: RowLeaseState.Settled } record
            && record.RetentionUntil > timeProvider.GetUtcNow()
                ? record.Result
                : null;

        return ValueTask.FromResult(result);
    }

    #endregion

    #region Purge

    public ValueTask<int> PurgeAsync(TimeSpan olderThan, int limit, CancellationToken cancellationToken = default)
    {
        Argument.IsPositiveOrZero(olderThan);
        Argument.IsPositive(limit);

        var now = timeProvider.GetUtcNow();

        // No retention can have ended before the earliest representable instant, so an age reaching past it deletes
        // nothing rather than overflowing.
        if (olderThan > now - DateTimeOffset.MinValue)
        {
            return ValueTask.FromResult(0);
        }

        var cutoff = now - olderThan;
        var deleted = 0;

        foreach (var (key, candidate) in Table.Rows)
        {
            if (deleted >= limit)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!_IsPurgeable(candidate, cutoff, now))
            {
                continue;
            }

            // A record a unit holds is left for a later purge rather than waited on, the in-memory form of SKIP
            // LOCKED: the purge never deletes a record under a unit that is deciding on it.
            using var held = Table.TryLock(key);

            if (held is null || Table.Read(key) is not { } record || !_IsPurgeable(record, cutoff, now))
            {
                continue;
            }

            Table.Write(key, row: null);
            deleted++;
        }

        return ValueTask.FromResult(deleted);
    }

    private static bool _IsPurgeable(InMemoryIdempotencyRecord record, DateTimeOffset cutoff, DateTimeOffset now)
    {
        // A live lease keeps its record even past retention, because its attempt may still complete.
        return record.RetentionUntil <= cutoff && !_IsLive(record.Lease, now);
    }

    #endregion

    #region Helpers

    private async ValueTask<UnitRowTransaction<IdempotencyRecordKey, InMemoryIdempotencyRecord>> _LockAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(unitOfWork);

        var transaction = Table.Join(unitOfWork);
        await transaction.LockAsync(key, cancellationToken).ConfigureAwait(false);

        return transaction;
    }

    private static IdempotencyRecordState _State(InMemoryIdempotencyRecord record, DateTimeOffset now, bool inserted)
    {
        // The same mapping the relational store reads: an active or settled lease names the generation that holds or
        // completed the record, and only an active one has an expiry the feature may judge.
        var lease = record.Lease;
        var holdsGeneration = lease.State is RowLeaseState.Active or RowLeaseState.Settled;
        var leaseExpiresAt = lease.State == RowLeaseState.Active ? lease.ExpiresAt : null;

        return new IdempotencyRecordState(
            inserted,
            lease.State == RowLeaseState.Settled ? IdempotencyRecordStatus.Completed : IdempotencyRecordStatus.Pending,
            record.Fingerprint,
            holdsGeneration ? lease.Generation : null,
            leaseExpiresAt,
            record.Result,
            record.RetentionUntil,
            IsRetentionElapsed: record.RetentionUntil <= now,
            IsLeaseLive: leaseExpiresAt > now,
            record.RecoveryPoint
        );
    }

    private static bool _IsLive(RowLeaseSnapshot lease, DateTimeOffset now)
    {
        return lease.State == RowLeaseState.Active && lease.ExpiresAt > now;
    }

    private static InMemoryIdempotencyRecord _Require(
        InMemoryIdempotencyRecord? record,
        IdempotencyRecordKey key,
        string verb
    )
    {
        return record ?? throw _NotWritten(key, verb);
    }

    private static void _RequireEnded(
        LeaseSettlementStatus status,
        LeaseSettlementStatus expected,
        IdempotencyRecordKey key,
        string verb
    )
    {
        if (status != expected)
        {
            throw _NotOwned(key, verb, status.ToString());
        }
    }

    private static InvalidOperationException _NotOwned(IdempotencyRecordKey key, string verb, string found)
    {
        // The caller locked the record and judged its lease in this unit, so the lease cannot have moved since;
        // reaching here means the caller skipped the lock, or ended the attempt one way and then tried the other.
        return new InvalidOperationException(
            $"Could not {verb} the idempotency record '{key.Key}': its lease was not current at the expected "
                + $"generation inside the unit that locked it (found: {found})."
        );
    }

    private static InvalidOperationException _NotWritten(IdempotencyRecordKey key, string verb)
    {
        // The caller locked or inserted the record in this unit, so it cannot have gone since; reaching here means the
        // caller skipped the lock.
        return new InvalidOperationException(
            $"Could not {verb} the idempotency record '{key.Key}': it was not found inside the unit that locked it."
        );
    }

    private static DateTimeOffset _Extend(DateTimeOffset current, DateTimeOffset now, TimeSpan retention)
    {
        // Retention only ever extends: a write sets it to the later of its current value and now plus the retention.
        var extended = _Add(now, retention);

        return extended > current ? extended : current;
    }

    private static DateTimeOffset _Add(DateTimeOffset instant, TimeSpan duration)
    {
        // Saturates instead of overflowing: an instant past the last representable one never comes anyway.
        return duration >= DateTimeOffset.MaxValue - instant ? DateTimeOffset.MaxValue : instant + duration;
    }

    internal static string LockName(IdempotencyRecordKey key)
    {
        // Length-prefixed, so no tenant and key can run together into another record's name.
        return string.Create(CultureInfo.InvariantCulture, $"{key.TenantId.Length}:{key.TenantId}{key.Key}");
    }

    #endregion
}
