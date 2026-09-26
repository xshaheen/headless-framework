// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Idempotency.InMemory;

/// <summary>
/// The in-memory record store: one process-local table of records, one exclusive lock per record key, and one
/// store-wide generation counter. Every verb that decides takes its key's lock and only then reads the injected
/// <see cref="TimeProvider" />, so a decision is never made on a clock read from before a lock wait.
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
        var now = timeProvider.GetUtcNow();

        if (transaction.Read(key) is { } existing)
        {
            return _State(existing, now, inserted: false);
        }

        var inserted = new InMemoryIdempotencyRecord(
            IdempotencyRecordStatus.Pending,
            fingerprint,
            Generation: null,
            LeaseExpiresAt: null,
            Result: null,
            _Add(now, retention)
        );
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

        return transaction.Read(key) is { } record ? _State(record, timeProvider.GetUtcNow(), inserted: false) : null;
    }

    #endregion

    #region Admit, complete, release

    public async ValueTask<IdempotencyRecordGrant> AdmitAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        TimeSpan leaseDuration,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(fingerprint);
        var transaction = await _LockAsync(unitOfWork, key, cancellationToken).ConfigureAwait(false);
        var record = transaction.Read(key) ?? throw _NotWritten(key, "admit");
        var now = timeProvider.GetUtcNow();

        // Drawn only now, under the record's lock: a generation drawn before the lock could be lower than one a
        // still-open admission already holds. Also the in-place reset of a record past its retention: every outcome
        // field is overwritten.
        var generation = storage.NextGeneration();
        var leaseExpiresAt = _Add(now, leaseDuration);

        transaction.Stage(
            key,
            new InMemoryIdempotencyRecord(
                IdempotencyRecordStatus.Pending,
                fingerprint,
                generation,
                leaseExpiresAt,
                Result: null,
                _Extend(record.RetentionUntil, now, retention)
            )
        );

        return new IdempotencyRecordGrant(generation, leaseExpiresAt);
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
        var record = _RequireGeneration(transaction.Read(key), key, generation, "complete");
        var now = timeProvider.GetUtcNow();

        // The completing generation is kept, so a second completion by the same attempt finds its own completed
        // record and is refused instead of overwriting the stored result. The result copies the caller's bytes.
        transaction.Stage(
            key,
            record with
            {
                Status = IdempotencyRecordStatus.Completed,
                LeaseExpiresAt = null,
                Result = new IdempotentResult(result.Span, contract),
                RetentionUntil = _Extend(record.RetentionUntil, now, retention),
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
        var record = _RequireGeneration(transaction.Read(key), key, generation, "release");
        var now = timeProvider.GetUtcNow();

        transaction.Stage(
            key,
            record with
            {
                Status = IdempotencyRecordStatus.Pending,
                Generation = null,
                LeaseExpiresAt = null,
                Result = null,
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

        var now = timeProvider.GetUtcNow();
        var isLive = record.LeaseExpiresAt > now;
        var status = IdempotencyLeaseClassifier.Classify(record.Status, record.Generation, isLive, generation);

        switch (status)
        {
            case IdempotentLeaseStatus.Current:
            {
                var renewedUntil = _Add(now, leaseDuration);
                Table.Write(key, record with { LeaseExpiresAt = renewedUntil });

                return new IdempotentLeaseRenewal(IdempotentLeaseStatus.Current, renewedUntil);
            }
            case IdempotentLeaseStatus.Expired:
                return new IdempotentLeaseRenewal(status, record.LeaseExpiresAt);
            default:
                return new IdempotentLeaseRenewal(status, ExpiresAt: null);
        }
    }

    public ValueTask<IdempotencyPeekStatus> PeekAsync(
        IdempotencyRecordKey key,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // No lock: the committed row is an immutable snapshot, so this never waits behind a unit that holds the key,
        // and a unit's uncommitted writes are invisible to it.
        if (Table.Read(key) is not { } record || record.RetentionUntil <= timeProvider.GetUtcNow())
        {
            return ValueTask.FromResult(IdempotencyPeekStatus.Absent);
        }

        return ValueTask.FromResult(
            record.Status == IdempotencyRecordStatus.Completed
                ? IdempotencyPeekStatus.Completed
                : IdempotencyPeekStatus.Pending
        );
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
        return record.RetentionUntil <= cutoff && (record.LeaseExpiresAt is null || record.LeaseExpiresAt <= now);
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
        return new IdempotencyRecordState(
            inserted,
            record.Status,
            record.Fingerprint,
            record.Generation,
            record.LeaseExpiresAt,
            record.Result,
            record.RetentionUntil,
            IsRetentionElapsed: record.RetentionUntil <= now,
            IsLeaseLive: record.LeaseExpiresAt > now
        );
    }

    private static InMemoryIdempotencyRecord _RequireGeneration(
        InMemoryIdempotencyRecord? record,
        IdempotencyRecordKey key,
        long generation,
        string verb
    )
    {
        return record is not null && record.Generation == generation ? record : throw _NotWritten(key, verb);
    }

    private static InvalidOperationException _NotWritten(IdempotencyRecordKey key, string verb)
    {
        // The caller locked the record and checked its generation in this unit, so the record cannot have changed
        // since; reaching here means the caller skipped the lock.
        return new InvalidOperationException(
            $"Could not {verb} the idempotency record '{key.Key}': it was not found at the expected generation "
                + "inside the unit that locked it."
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
