// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Fencing.InMemory;

/// <summary>
/// The in-memory lease store: one process-local table of lease rows, one exclusive lock per lease key, and one
/// store-wide generation counter. Every verb takes its key's lock and only then reads the injected
/// <see cref="TimeProvider" />, once, so a decision is never made on a clock read from before a lock wait.
/// </summary>
/// <remarks>
/// <para>
/// The injected clock is the authority here, the single-process counterpart of the relational providers' database
/// clock. State lives in this singleton and disappears with the process, so it coordinates the callers of one process
/// only.
/// </para>
/// <para>
/// An enlisted call runs on a unit that carries no relational resource: the unit itself is the commit boundary. The
/// key's lock is held until the unit ends, the call's writes reach the table only when the unit completes, and a
/// rollback or an abandoned unit drops them. A unit whose work commits in a database is refused, because lease state
/// in this process cannot commit or roll back with that transaction.
/// </para>
/// </remarks>
internal sealed class InMemoryLeaseStore(
    InMemoryLeaseStorage storage,
    IUnitOfWorkFactory unitOfWorkFactory,
    TimeProvider timeProvider
) : ILeaseStore
{
    private InMemoryRowTable<LeaseKey, InMemoryLease> Table => storage.Table;

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
                "The unit of work runs on a database transaction, but Headless.Fencing.InMemory keeps leases in "
                    + "process memory, which cannot commit or roll back with it. Call enlisted lease methods on a "
                    + "resource-less unit (IUnitOfWorkFactory.BeginAsync()), or use IFencedLeases for autonomous calls."
            );
        }
    }

    #endregion

    #region Grant

    public async ValueTask<LeaseGrantResult> GrantAsync(
        LeaseKey key,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        using var held = await Table.LockAsync(key, cancellationToken).ConfigureAwait(false);

        var (result, row) = _Grant(Table.Read(key), key, duration);

        if (row is not null)
        {
            Table.Write(key, row);
        }

        return result;
    }

    public async ValueTask<LeaseGrantResult> GrantEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        var transaction = await _LockAsync(unitOfWork, key, cancellationToken).ConfigureAwait(false);

        var (result, row) = _Grant(transaction.Read(key), key, duration);

        if (row is not null)
        {
            transaction.Stage(key, row);
        }

        return result;
    }

    private (LeaseGrantResult Result, InMemoryLease? Row) _Grant(
        InMemoryLease? existing,
        LeaseKey key,
        TimeSpan duration
    )
    {
        var now = timeProvider.GetUtcNow();

        if (existing is { State: InMemoryLeaseState.Active } live && live.ExpiresAt > now)
        {
            return (LeaseGrantResult.Held(live.Generation, live.ExpiresAt), null);
        }

        // Drawn only now, under the key's lock: a generation drawn before the lock could be lower than one a
        // still-open grant already holds.
        var generation = storage.NextGeneration();
        var row = new InMemoryLease(generation, InMemoryLeaseState.Active, now, _Add(now, duration), EndedAt: null);
        var lease = key.ToLease(generation);

        var result = existing is { State: InMemoryLeaseState.Active } expired
            ? LeaseGrantResult.Takeover(lease, row.ExpiresAt, expired.Generation)
            : LeaseGrantResult.Granted(lease, row.ExpiresAt);

        return (result, row);
    }

    #endregion

    #region Renew, settle, release

    public async ValueTask<LeaseRenewalResult> RenewAsync(
        LeaseKey key,
        long generation,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        using var held = await Table.LockAsync(key, cancellationToken).ConfigureAwait(false);

        var (result, row) = _Renew(Table.Read(key), generation, duration);

        if (row is not null)
        {
            Table.Write(key, row);
        }

        return result;
    }

    public async ValueTask<LeaseRenewalResult> RenewEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        var transaction = await _LockAsync(unitOfWork, key, cancellationToken).ConfigureAwait(false);

        var (result, row) = _Renew(transaction.Read(key), generation, duration);

        if (row is not null)
        {
            transaction.Stage(key, row);
        }

        return result;
    }

    public async ValueTask<LeaseSettlementStatus> SettleAsync(
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        using var held = await Table.LockAsync(key, cancellationToken).ConfigureAwait(false);

        var (status, row) = _End(Table.Read(key), generation, InMemoryLeaseState.Settled);

        if (row is not null)
        {
            Table.Write(key, row);
        }

        return status;
    }

    public async ValueTask<LeaseSettlementStatus> SettleEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        var transaction = await _LockAsync(unitOfWork, key, cancellationToken).ConfigureAwait(false);

        var (status, row) = _End(transaction.Read(key), generation, InMemoryLeaseState.Settled);

        if (row is not null)
        {
            transaction.Stage(key, row);
        }

        return status;
    }

    public async ValueTask<LeaseSettlementStatus> ReleaseAsync(
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        using var held = await Table.LockAsync(key, cancellationToken).ConfigureAwait(false);

        var (status, row) = _End(Table.Read(key), generation, InMemoryLeaseState.Released);

        if (row is not null)
        {
            Table.Write(key, row);
        }

        return status;
    }

    public async ValueTask<LeaseSettlementStatus> ReleaseEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        var transaction = await _LockAsync(unitOfWork, key, cancellationToken).ConfigureAwait(false);

        var (status, row) = _End(transaction.Read(key), generation, InMemoryLeaseState.Released);

        if (row is not null)
        {
            transaction.Stage(key, row);
        }

        return status;
    }

    private (LeaseRenewalResult Result, InMemoryLease? Row) _Renew(
        InMemoryLease? existing,
        long generation,
        TimeSpan duration
    )
    {
        var now = timeProvider.GetUtcNow();
        var renewedUntil = _Add(now, duration);

        return _Classify(existing, generation, now) switch
        {
            LeaseFenceStatus.Current => (
                new LeaseRenewalResult(LeaseRenewalStatus.Renewed, renewedUntil),
                existing! with
                {
                    ExpiresAt = renewedUntil,
                }
            ),
            LeaseFenceStatus.Expired => (new LeaseRenewalResult(LeaseRenewalStatus.Expired, existing!.ExpiresAt), null),
            LeaseFenceStatus.Settled => (new LeaseRenewalResult(LeaseRenewalStatus.Settled, ExpiresAt: null), null),
            LeaseFenceStatus.Released => (new LeaseRenewalResult(LeaseRenewalStatus.Released, ExpiresAt: null), null),
            LeaseFenceStatus.Abandoned => (new LeaseRenewalResult(LeaseRenewalStatus.Abandoned, ExpiresAt: null), null),
            _ => (new LeaseRenewalResult(LeaseRenewalStatus.Stale, ExpiresAt: null), null),
        };
    }

    private (LeaseSettlementStatus Status, InMemoryLease? Row) _End(
        InMemoryLease? existing,
        long generation,
        InMemoryLeaseState ended
    )
    {
        var now = timeProvider.GetUtcNow();

        // A settled or released row at this generation reports its state whichever verb asked, so a retried
        // settlement reports success and a release after a settlement reports that the attempt settled.
        return _Classify(existing, generation, now) switch
        {
            LeaseFenceStatus.Current => (
                ended == InMemoryLeaseState.Settled ? LeaseSettlementStatus.Settled : LeaseSettlementStatus.Released,
                existing! with
                {
                    State = ended,
                    EndedAt = now,
                }
            ),
            LeaseFenceStatus.Settled => (LeaseSettlementStatus.Settled, null),
            LeaseFenceStatus.Released => (LeaseSettlementStatus.Released, null),
            LeaseFenceStatus.Abandoned => (LeaseSettlementStatus.Abandoned, null),
            LeaseFenceStatus.Expired => (LeaseSettlementStatus.Expired, null),
            _ => (LeaseSettlementStatus.Stale, null),
        };
    }

    #endregion

    #region Fence

    public async ValueTask<LeaseFenceStatus> FenceEnlistedAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        long generation,
        CancellationToken cancellationToken = default
    )
    {
        // The key's lock stays with the unit until it ends, so the answer holds for every later call the unit makes
        // in this process. It guards nothing outside the process.
        var transaction = await _LockAsync(unitOfWork, key, cancellationToken).ConfigureAwait(false);

        return _Classify(transaction.Read(key), generation, timeProvider.GetUtcNow());
    }

    #endregion

    #region Sweep and purge

    public ValueTask<ExpiredLease?> ClaimExpiredEnlistedAsync(
        IUnitOfWork unitOfWork,
        string kind,
        ExpiredLease? after,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);
        Argument.IsNotNull(kind);
        cancellationToken.ThrowIfCancellationRequested();

        var transaction = Table.Join(unitOfWork);
        var now = timeProvider.GetUtcNow();
        var cursor = after is null
            ? (LeaseCursor?)null
            : new LeaseCursor(after.ExpiresAt, after.TenantId ?? "", after.Resource);

        var candidates = Table
            .Rows.Where(pair => _IsClaimable(pair.Key, pair.Value, kind, now, cursor))
            .Select(static pair => new LeaseCursor(pair.Value.ExpiresAt, pair.Key.TenantId, pair.Key.Resource))
            .Order()
            .ToList();

        foreach (var candidate in candidates)
        {
            var key = new LeaseKey(candidate.TenantId, kind, candidate.Resource);

            // A key another unit holds (another sweeper's claim, a fence, an open grant) is passed over rather than
            // waited on, so concurrent sweepers never hand one lease to two handlers and never wait on each other.
            if (!transaction.TryLock(key))
            {
                continue;
            }

            // Re-read under the lock: the row may have been taken over, settled, or purged since the scan.
            var row = transaction.Read(key);

            if (row is null || !_IsClaimable(key, row, kind, now, cursor))
            {
                transaction.Unlock(key);

                continue;
            }

            transaction.Stage(key, row with { State = InMemoryLeaseState.Abandoned, EndedAt = now });

            return ValueTask.FromResult<ExpiredLease?>(key.ToExpiredLease(row.Generation, row.ExpiresAt));
        }

        return ValueTask.FromResult<ExpiredLease?>(null);
    }

    public ValueTask<int> PurgeAsync(string kind, TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(kind);
        Argument.IsPositiveOrZero(olderThan);

        var now = timeProvider.GetUtcNow();

        // No lease can have ended before the earliest representable instant, so an age reaching past it deletes
        // nothing rather than overflowing.
        if (olderThan > now - DateTimeOffset.MinValue)
        {
            return ValueTask.FromResult(0);
        }

        var cutoff = now - olderThan;
        var deleted = 0;

        foreach (var (key, candidate) in Table.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_IsPurgeable(key, candidate, kind, cutoff))
            {
                continue;
            }

            // A key a unit holds is left for a later purge, the in-memory form of SKIP LOCKED.
            using var held = Table.TryLock(key);

            if (held is null || Table.Read(key) is not { } row || !_IsPurgeable(key, row, kind, cutoff))
            {
                continue;
            }

            Table.Write(key, row: null);
            deleted++;
        }

        return ValueTask.FromResult(deleted);
    }

    private static bool _IsClaimable(
        LeaseKey key,
        InMemoryLease row,
        string kind,
        DateTimeOffset now,
        LeaseCursor? cursor
    )
    {
        return string.Equals(key.Kind, kind, StringComparison.Ordinal)
            && row.State == InMemoryLeaseState.Active
            && row.ExpiresAt <= now
            && (
                cursor is null || new LeaseCursor(row.ExpiresAt, key.TenantId, key.Resource).CompareTo(cursor.Value) > 0
            );
    }

    private static bool _IsPurgeable(LeaseKey key, InMemoryLease row, string kind, DateTimeOffset cutoff)
    {
        return string.Equals(key.Kind, kind, StringComparison.Ordinal)
            && row.State != InMemoryLeaseState.Active
            && row.EndedAt <= cutoff;
    }

    #endregion

    #region Helpers

    private async ValueTask<UnitRowTransaction<LeaseKey, InMemoryLease>> _LockAsync(
        IUnitOfWork unitOfWork,
        LeaseKey key,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(unitOfWork);

        var transaction = Table.Join(unitOfWork);
        await transaction.LockAsync(key, cancellationToken).ConfigureAwait(false);

        return transaction;
    }

    private static LeaseFenceStatus _Classify(InMemoryLease? row, long generation, DateTimeOffset now)
    {
        if (row is null || row.Generation != generation)
        {
            return LeaseFenceStatus.Stale;
        }

        return row.State switch
        {
            InMemoryLeaseState.Settled => LeaseFenceStatus.Settled,
            InMemoryLeaseState.Released => LeaseFenceStatus.Released,
            InMemoryLeaseState.Abandoned => LeaseFenceStatus.Abandoned,
            _ when row.ExpiresAt > now => LeaseFenceStatus.Current,
            _ => LeaseFenceStatus.Expired,
        };
    }

    private static DateTimeOffset _Add(DateTimeOffset instant, TimeSpan duration)
    {
        // Saturates instead of overflowing: an expiry past the last representable instant never comes anyway.
        return duration >= DateTimeOffset.MaxValue - instant ? DateTimeOffset.MaxValue : instant + duration;
    }

    internal static string LockName(LeaseKey key)
    {
        // Length-prefixed, so no tenant, kind, and resource can run together into another key's name.
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{key.TenantId.Length}:{key.TenantId}{key.Kind.Length}:{key.Kind}{key.Resource}"
        );
    }

    #endregion

    /// <summary>The sweep's visiting order: expiry, then tenant, then resource, each compared ordinally.</summary>
    private readonly record struct LeaseCursor(DateTimeOffset ExpiresAt, string TenantId, string Resource)
        : IComparable<LeaseCursor>
    {
        public int CompareTo(LeaseCursor other)
        {
            var byExpiry = ExpiresAt.CompareTo(other.ExpiresAt);

            if (byExpiry != 0)
            {
                return byExpiry;
            }

            var byTenant = string.CompareOrdinal(TenantId, other.TenantId);

            return byTenant != 0 ? byTenant : string.CompareOrdinal(Resource, other.Resource);
        }
    }
}
