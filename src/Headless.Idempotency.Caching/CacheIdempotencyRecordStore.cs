// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Runtime.CompilerServices;
using Headless.Caching;
using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Idempotency.Caching;

/// <summary>
/// The cache-backed record store: one <see cref="ICache" /> entry per record key holding the whole record, changed only
/// by compare-and-swap against the exact entry a call read, and one cache counter for generations.
/// </summary>
/// <remarks>
/// <para>
/// A cache holds no row lock, so this store is optimistic where the relational stores lock: a verb that "locks" a
/// record only reads it and remembers what it read, and the write verb that follows swaps the entry only if it still
/// holds that exact text. When another caller changed it first, the write throws
/// <see cref="IdempotencyRecordConflictException" /> and the autonomous call runs again from a fresh read, so concurrent
/// admissions converge the way locked ones do: one admitted, the others in flight, replayed, or in conflict.
/// </para>
/// <para>
/// A cache cannot commit or roll back with a unit of work, so the only units this store accepts are the resource-less
/// ones it begins itself for an autonomous call. In such a unit each write applies at once and is the call's commit;
/// the call's own unit completes right after it, and a call that decides not to write rolls back a unit that holds
/// nothing. Every caller unit, and so every <c>unit.Idempotency</c> call, is refused.
/// </para>
/// <para>
/// Leases and retention are decided by the injected <see cref="TimeProvider" />, read after the entry is read: the
/// application clock, not a database clock, so two replicas with skewed clocks see a lease expire at different times.
/// Each entry lives in the cache until the later of its retention and its lease, so the cache's expiry is the purge.
/// </para>
/// </remarks>
internal sealed class CacheIdempotencyRecordStore(
    ICache cache,
    IUnitOfWorkFactory unitOfWorkFactory,
    CacheIdempotencyOptions options,
    TimeProvider timeProvider
) : IIdempotencyRecordStore
{
    internal const string EnlistedRefusal =
        "Headless.Idempotency.Caching keeps idempotency records in a cache, which cannot commit or roll back with a "
        + "unit of work, so it serves only the autonomous IIdempotentOperations calls. unit.Idempotency (an enlisted "
        + "admission, completion, release, FenceAsync, or SetRecoveryPointAsync inside a unit) needs a provider that "
        + "joins the unit: UsePostgreSql, UseSqlServer, or UseInMemory.";

    // A renewal is its own autonomous call, so it retries its own compare-and-swap. Each lost race means another write
    // to the record landed, so the bound only stops a heartbeat that keeps losing to a stream of writers.
    private const int _MaxRenewAttempts = 8;

    // The cache expiry only collects entries; the record's own instants decide everything. Capped so a retention near
    // the largest TimeSpan never overflows a cache's expiry arithmetic, and floored because caches refuse a non-positive
    // expiry.
    private static readonly TimeSpan _MaxEntryLifetime = TimeSpan.FromDays(36_500);
    private static readonly TimeSpan _MinEntryLifetime = TimeSpan.FromSeconds(1);

    private readonly ConditionalWeakTable<IUnitOfWork, OwnedUnit> _ownedUnits = [];
    private readonly string _keyPrefix = options.KeyPrefix;
    private readonly string _generationKey = GenerationKey(options.KeyPrefix);

    #region Owned units and enlistment

    public async ValueTask<IUnitOfWork> BeginOwnedUnitAsync(CancellationToken cancellationToken = default)
    {
        var unit = await unitOfWorkFactory.BeginAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        _ownedUnits.Add(unit, new OwnedUnit());

        return unit;
    }

    public void ValidateEnlistment(IUnitOfWork unitOfWork)
    {
        Argument.IsNotNull(unitOfWork);

        if (!_ownedUnits.TryGetValue(unitOfWork, out _))
        {
            throw new InvalidOperationException(EnlistedRefusal);
        }
    }

    #endregion

    #region Read

    public async ValueTask<IdempotencyRecordState> LockOrInsertAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        IdempotencyFingerprint fingerprint,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(fingerprint);
        var unit = _Owned(unitOfWork);
        var (raw, entry) = await _ReadAsync(key, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        unit.Remember(key, raw);

        if (entry is not null)
        {
            return _State(entry, now, inserted: false);
        }

        // Nothing is written yet: an absent record is always admitted next, and that admission inserts it only if the
        // key is still absent, so a concurrent insert makes it retry rather than overwrite.
        var inserted = new CacheIdempotencyEntry(
            IdempotencyRecordStatus.Pending,
            fingerprint.Algorithm,
            fingerprint.Hash.ToArray(),
            Generation: null,
            LeaseExpiresAt: null,
            Result: null,
            ResultContract: null,
            _Add(now, retention),
            RecoveryPoint: null,
            RecoveryState: null,
            RecoveryContract: null,
            HighestGeneration: null
        );

        return _State(inserted, now, inserted: true);
    }

    public async ValueTask<IdempotencyRecordState?> LockAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        CancellationToken cancellationToken = default
    )
    {
        var unit = _Owned(unitOfWork);
        var (raw, entry) = await _ReadAsync(key, cancellationToken).ConfigureAwait(false);
        unit.Remember(key, raw);

        return entry is null ? null : _State(entry, timeProvider.GetUtcNow(), inserted: false);
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
        var unit = _Owned(unitOfWork);
        var read = unit.Read(key, "admit");
        var record = read is null ? null : CacheIdempotencyEntry.Deserialize(_RecordKey(key), read);

        var generation = await _NextGenerationAsync(record?.HighestGeneration, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var leaseExpiresAt = _Add(now, leaseDuration);
        var retentionUntil = record is null ? _Add(now, retention) : _Extend(record.RetentionUntil, now, retention);
        var keep = keepRecoveryPoint && record is not null;

        // Every outcome field is overwritten, which is also the in-place reset of a record past its retention.
        var admitted = new CacheIdempotencyEntry(
            IdempotencyRecordStatus.Pending,
            fingerprint.Algorithm,
            fingerprint.Hash.ToArray(),
            generation,
            leaseExpiresAt,
            Result: null,
            ResultContract: null,
            retentionUntil,
            keep ? record!.RecoveryPoint : null,
            keep ? record!.RecoveryState : null,
            keep ? record!.RecoveryContract : null,
            HighestGeneration: generation
        );

        await _SwapAsync(unit, key, read, admitted, now, cancellationToken).ConfigureAwait(false);

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
        var unit = _Owned(unitOfWork);
        var read = unit.Read(key, "complete");
        var record = _RequireGeneration(read, key, generation, "complete");
        var now = timeProvider.GetUtcNow();

        // The completing generation is kept, so a second completion by the same attempt finds its own completed record
        // and is refused instead of overwriting the stored result.
        var completed = record with
        {
            Status = IdempotencyRecordStatus.Completed,
            LeaseExpiresAt = null,
            Result = result.ToArray(),
            ResultContract = contract,
            RetentionUntil = _Extend(record.RetentionUntil, now, retention),
            RecoveryPoint = null,
            RecoveryState = null,
            RecoveryContract = null,
        };

        await _SwapAsync(unit, key, read, completed, now, cancellationToken).ConfigureAwait(false);
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
        var unit = _Owned(unitOfWork);
        var read = unit.Read(key, "set the recovery point of");
        var record = _RequireGeneration(read, key, generation, "set the recovery point of");

        var recorded = record with
        {
            RecoveryPoint = point,
            RecoveryState = state.ToArray(),
            RecoveryContract = contract,
        };

        await _SwapAsync(unit, key, read, recorded, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReleaseAsync(
        IUnitOfWork unitOfWork,
        IdempotencyRecordKey key,
        long generation,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    )
    {
        var unit = _Owned(unitOfWork);
        var read = unit.Read(key, "release");
        var record = _RequireGeneration(read, key, generation, "release");
        var now = timeProvider.GetUtcNow();

        var released = record with
        {
            Status = IdempotencyRecordStatus.Pending,
            Generation = null,
            LeaseExpiresAt = null,
            Result = null,
            ResultContract = null,
            RetentionUntil = _Extend(record.RetentionUntil, now, retention),
        };

        await _SwapAsync(unit, key, read, released, now, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Renew, peek, purge

    public async ValueTask<IdempotentLeaseRenewal> RenewAsync(
        IdempotencyRecordKey key,
        long generation,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            var (raw, record) = await _ReadAsync(key, cancellationToken).ConfigureAwait(false);

            if (record is null)
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
                    var renewed = record with { LeaseExpiresAt = renewedUntil };

                    if (await _TrySwapAsync(key, raw, renewed, now, cancellationToken).ConfigureAwait(false))
                    {
                        return new IdempotentLeaseRenewal(IdempotentLeaseStatus.Current, renewedUntil);
                    }

                    if (attempt >= _MaxRenewAttempts)
                    {
                        throw new IdempotencyRecordConflictException(key);
                    }

                    // Another write landed first; classify again against what it wrote.
                    continue;
                }
                case IdempotentLeaseStatus.Expired:
                    return new IdempotentLeaseRenewal(status, record.LeaseExpiresAt);
                default:
                    return new IdempotentLeaseRenewal(status, ExpiresAt: null);
            }
        }
    }

    public async ValueTask<IdempotencyPeekStatus> PeekAsync(
        IdempotencyRecordKey key,
        CancellationToken cancellationToken = default
    )
    {
        var (_, record) = await _ReadAsync(key, cancellationToken).ConfigureAwait(false);

        if (record is null || record.RetentionUntil <= timeProvider.GetUtcNow())
        {
            return IdempotencyPeekStatus.Absent;
        }

        return record.Status == IdempotencyRecordStatus.Completed
            ? IdempotencyPeekStatus.Completed
            : IdempotencyPeekStatus.Pending;
    }

    public ValueTask<int> PurgeAsync(TimeSpan olderThan, int limit, CancellationToken cancellationToken = default)
    {
        Argument.IsPositiveOrZero(olderThan);
        Argument.IsPositive(limit);

        // Every entry expires in the cache at the later of its retention and its lease, which is exactly when the
        // purge would take it, so there is never anything left to delete here.
        return ValueTask.FromResult(0);
    }

    #endregion

    #region Helpers

    /// <summary>The cache key of <paramref name="key" />'s record.</summary>
    internal static string RecordKey(string keyPrefix, IdempotencyRecordKey key)
    {
        // Length-prefixed, so no tenant and key can run together into another record's key.
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{keyPrefix}record:{key.TenantId.Length}:{key.TenantId}{key.Key}"
        );
    }

    /// <summary>The cache key of the store-wide generation counter.</summary>
    internal static string GenerationKey(string keyPrefix)
    {
        return keyPrefix + "generation";
    }

    private string _RecordKey(IdempotencyRecordKey key)
    {
        return RecordKey(_keyPrefix, key);
    }

    private OwnedUnit _Owned(IUnitOfWork unitOfWork)
    {
        Argument.IsNotNull(unitOfWork);

        return _ownedUnits.TryGetValue(unitOfWork, out var unit)
            ? unit
            : throw new InvalidOperationException(EnlistedRefusal);
    }

    private async ValueTask<(string? Raw, CacheIdempotencyEntry? Record)> _ReadAsync(
        IdempotencyRecordKey key,
        CancellationToken cancellationToken
    )
    {
        var cacheKey = _RecordKey(key);
        var value = await cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false);

        return value is { HasValue: true, Value: { } raw }
            ? (raw, CacheIdempotencyEntry.Deserialize(cacheKey, raw))
            : (null, null);
    }

    private async ValueTask<long> _NextGenerationAsync(long? stored, CancellationToken cancellationToken)
    {
        // The counter never expires, but a cache under memory pressure may still evict it and restart it from one.
        // Never issuing a generation at or below the highest the record ever admitted keeps the key's generations
        // growing even then, and raising the counter past it keeps the next draw from repeating this one. Only losing
        // the counter and the record together can repeat a generation.
        var drawn = await cache
            .IncrementAsync(_generationKey, 1L, expiration: null, cancellationToken)
            .ConfigureAwait(false);

        if (stored is { } current && drawn <= current)
        {
            drawn = current + 1;
            await cache
                .SetIfHigherAsync(_generationKey, drawn, expiration: null, cancellationToken)
                .ConfigureAwait(false);
        }

        return drawn;
    }

    private async ValueTask _SwapAsync(
        OwnedUnit unit,
        IdempotencyRecordKey key,
        string? expected,
        CacheIdempotencyEntry record,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var raw = record.Serialize();

        if (!await _TrySwapAsync(key, expected, raw, record, now, cancellationToken).ConfigureAwait(false))
        {
            throw new IdempotencyRecordConflictException(key);
        }

        // A later write in the same unit compares against this one, not against what the unit first read.
        unit.Remember(key, raw);
    }

    private ValueTask<bool> _TrySwapAsync(
        IdempotencyRecordKey key,
        string? expected,
        CacheIdempotencyEntry record,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        return _TrySwapAsync(key, expected, record.Serialize(), record, now, cancellationToken);
    }

    /// <summary>
    /// Writes <paramref name="raw" /> only if the entry still holds <paramref name="expected" /> (absent when
    /// <see langword="null" />), and reports whether it did. A write the cache refused while the entry still held the
    /// expected text is not a lost race, so it throws instead of reporting one.
    /// </summary>
    /// <exception cref="InvalidOperationException">The cache refused the write for a reason other than a race.</exception>
    private async ValueTask<bool> _TrySwapAsync(
        IdempotencyRecordKey key,
        string? expected,
        string raw,
        CacheIdempotencyEntry record,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var cacheKey = _RecordKey(key);
        var lifetime = _Lifetime(record, now);

        var swapped = expected is null
            ? await cache.TryInsertAsync(cacheKey, raw, lifetime, cancellationToken).ConfigureAwait(false)
            : await cache
                .TryReplaceIfEqualAsync(cacheKey, expected, raw, lifetime, cancellationToken)
                .ConfigureAwait(false);

        if (swapped)
        {
            return true;
        }

        var current = await cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false);
        var unchanged = current is { HasValue: true, Value: { } value }
            ? string.Equals(value, expected, StringComparison.Ordinal)
            : expected is null;

        if (unchanged)
        {
            // The entry is exactly what this call read, so no other writer won; the cache itself declined the write,
            // for example an in-memory cache refusing an entry over its size limit. Retrying would only repeat that.
            throw new InvalidOperationException(
                $"The cache declined to store the idempotency record for key '{key.Key}' although no other writer "
                    + "changed it; check the cache's entry size and capacity limits."
            );
        }

        return false;
    }

    private static TimeSpan _Lifetime(CacheIdempotencyEntry record, DateTimeOffset now)
    {
        // Kept until the later of its retention and its lease: a live attempt whose lease outlasts the retention must
        // still find its record when it completes.
        var until = record.LeaseExpiresAt > record.RetentionUntil ? record.LeaseExpiresAt.Value : record.RetentionUntil;
        var lifetime = until - now;

        if (lifetime < _MinEntryLifetime)
        {
            return _MinEntryLifetime;
        }

        return lifetime > _MaxEntryLifetime ? _MaxEntryLifetime : lifetime;
    }

    private static IdempotencyRecordState _State(CacheIdempotencyEntry record, DateTimeOffset now, bool inserted)
    {
        return new IdempotencyRecordState(
            inserted,
            record.Status,
            new IdempotencyFingerprint(record.FingerprintAlgorithm, record.Fingerprint),
            record.Generation,
            record.LeaseExpiresAt,
            record.Result is null ? null : new IdempotentResult(record.Result, record.ResultContract ?? string.Empty),
            record.RetentionUntil,
            IsRetentionElapsed: record.RetentionUntil <= now,
            IsLeaseLive: record.LeaseExpiresAt > now,
            record.RecoveryPoint is null
                ? null
                : new IdempotentRecoveryPoint(
                    record.RecoveryPoint,
                    record.RecoveryState ?? [],
                    record.RecoveryContract ?? string.Empty
                )
        );
    }

    private CacheIdempotencyEntry _RequireGeneration(
        string? read,
        IdempotencyRecordKey key,
        long generation,
        string verb
    )
    {
        var record = read is null ? null : CacheIdempotencyEntry.Deserialize(_RecordKey(key), read);

        // The caller read the record in this unit and checked its generation, so reaching here means it skipped that.
        return record is not null && record.Generation == generation
            ? record
            : throw new InvalidOperationException(
                $"Could not {verb} the idempotency record '{key.Key}': it was not found at the expected generation "
                    + "inside the unit that read it."
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

    #endregion

    /// <summary>
    /// What one owned unit read: the exact entry text per record key (<see langword="null" /> when absent), which the
    /// unit's write compares against.
    /// </summary>
    private sealed class OwnedUnit
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<IdempotencyRecordKey, string?> _read = [];

        public void Remember(IdempotencyRecordKey key, string? raw)
        {
            lock (_gate)
            {
                _read[key] = raw;
            }
        }

        public string? Read(IdempotencyRecordKey key, string verb)
        {
            lock (_gate)
            {
                return _read.TryGetValue(key, out var raw)
                    ? raw
                    : throw new InvalidOperationException(
                        $"Could not {verb} the idempotency record '{key.Key}': this unit never read it."
                    );
            }
        }
    }
}
