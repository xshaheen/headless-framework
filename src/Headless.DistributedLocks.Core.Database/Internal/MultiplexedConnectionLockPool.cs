// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Nito.AsyncEx;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// A pool of <see cref="MultiplexedConnectionLock"/> instances keyed by connection string. Lets an uncontended acquire
/// reuse a connection that is already holding a lock for the same connection string, and prunes idle/disposable
/// instances to bound memory growth.
/// </summary>
internal sealed class MultiplexedConnectionLockPool
{
    // Only LockAsync is needed here (no zero-wait/timed acquire), so Nito.AsyncEx.AsyncLock is appropriate.
    private readonly AsyncLock _lock = new();
    private readonly Dictionary<string, Queue<MultiplexedConnectionLock>> _poolsByConnectionString = new(
        StringComparer.Ordinal
    );

    // Number of StoreOrDisposeLockAsync calls since the last prune; one "ticket" per call used to amortize pruning cost.
    private uint _storeCountSinceLastPrune;

    // Number of MultiplexedConnectionLock instances currently stored across all pools.
    private uint _pooledLockCount;

    /// <summary>
    /// Initializes the pool with the given connection factory.
    /// </summary>
    /// <param name="connectionFactory">
    /// Factory that creates a <see cref="DatabaseConnection"/> for a given connection string. Called
    /// when the pool has no idle lock to reuse for that connection string.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connectionFactory"/> is <see langword="null"/>.</exception>
    public MultiplexedConnectionLockPool(Func<string, DatabaseConnection> connectionFactory)
    {
        ConnectionFactory = Argument.IsNotNull(connectionFactory);
    }

    /// <summary>The factory used to create new connections for a given connection string.</summary>
    internal Func<string, DatabaseConnection> ConnectionFactory { get; }

    /// <summary>
    /// Attempts to acquire an advisory lock for <paramref name="name"/> on a pooled connection for
    /// <paramref name="connectionString"/>. First tries an opportunistic acquire on an existing pooled lock;
    /// on failure or absence of a pooled lock, opens a fresh connection.
    /// </summary>
    /// <typeparam name="TLockCookie">The strategy's opaque acquire/release state.</typeparam>
    /// <param name="connectionString">The connection string that keys the pool bucket.</param>
    /// <param name="name">The advisory lock resource name.</param>
    /// <param name="timeout">The full acquire timeout for non-opportunistic attempts.</param>
    /// <param name="strategy">The SQL synchronization strategy.</param>
    /// <param name="keepaliveCadence">Keepalive interval for the held connection.</param>
    /// <param name="cancellationToken">Token used to cancel connection open and strategy acquire.</param>
    /// <returns>A live <see cref="IDistributedLease"/> on success, or <see langword="null"/> on failure.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async ValueTask<IDistributedLease?> TryAcquireAsync<TLockCookie>(
        string connectionString,
        string name,
        TimeSpan timeout,
        IDbSynchronizationStrategy<TLockCookie> strategy,
        TimeSpan keepaliveCadence,
        CancellationToken cancellationToken
    )
        where TLockCookie : class
    {
        // Opportunistic phase: try to reuse a connection that is already holding a lock to acquire this one too.
        var existingLock = await _GetExistingLockOrDefaultAsync(connectionString).ConfigureAwait(false);

        if (existingLock is not null)
        {
            var canSafelyDisposeExistingLock = false;

            try
            {
                var opportunisticResult = await tryAcquireAsync(existingLock, opportunistic: true)
                    .ConfigureAwait(false);

                if (opportunisticResult.Handle is not null)
                {
                    return opportunisticResult.Handle;
                }

                // Always false when a handle was returned, so it is safe to read it only on the no-handle path.
                canSafelyDisposeExistingLock = opportunisticResult.CanSafelyDispose;

                switch (opportunisticResult.Retry)
                {
                    case MultiplexedConnectionLockRetry.NoRetry:
                        return null;
                    case MultiplexedConnectionLockRetry.RetryOnThisLock:
                        var retryOnThisLockResult = await tryAcquireAsync(existingLock, opportunistic: false)
                            .ConfigureAwait(false);
                        canSafelyDisposeExistingLock = retryOnThisLockResult.CanSafelyDispose;

                        return retryOnThisLockResult.Handle;
                    case MultiplexedConnectionLockRetry.Retry:
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected retry value.");
                }
            }
            finally
            {
                // We took this lock from the pool, so always return it (or dispose if it is no longer useful).
                await _StoreOrDisposeLockAsync(
                        connectionString,
                        existingLock,
                        shouldDispose: canSafelyDisposeExistingLock
                    )
                    .ConfigureAwait(false);
            }
        }

        // Normal phase: a fresh connection with the full timeout. The lock is always stored or disposed in the finally.
#pragma warning disable CA2000
        var @lock = new MultiplexedConnectionLock(ConnectionFactory(connectionString));
#pragma warning restore CA2000
        MultiplexedConnectionLock.Result? result = null;

        try
        {
            result = await tryAcquireAsync(@lock, opportunistic: false).ConfigureAwait(false);
            Debug.Assert(
                result.Value.Retry == MultiplexedConnectionLockRetry.NoRetry,
                "Acquire on a fresh lock should not recommend a retry."
            );
        }
        finally
        {
            // If we failed to even produce a result on a brand-new lock, there is no reason to store it.
            await _StoreOrDisposeLockAsync(connectionString, @lock, shouldDispose: result?.CanSafelyDispose ?? true)
                .ConfigureAwait(false);
        }

        return result.Value.Handle;

        ValueTask<MultiplexedConnectionLock.Result> tryAcquireAsync(
            MultiplexedConnectionLock instance,
            bool opportunistic
        ) => instance.TryAcquireAsync(name, timeout, strategy, keepaliveCadence, opportunistic, cancellationToken);
    }

    private async ValueTask<MultiplexedConnectionLock?> _GetExistingLockOrDefaultAsync(string connectionString)
    {
        using var _ = await _lock.LockAsync(CancellationToken.None).ConfigureAwait(false);

        if (_poolsByConnectionString.TryGetValue(connectionString, out var pool) && pool.Count != 0)
        {
            --_pooledLockCount;

            return pool.Dequeue();
        }

        return null;
    }

    private async ValueTask _StoreOrDisposeLockAsync(
        string connectionString,
        MultiplexedConnectionLock @lock,
        bool shouldDispose
    )
    {
        if (shouldDispose)
        {
            await _SuppressDisposeAsync(@lock).ConfigureAwait(false);
        }

        using (await _lock.LockAsync(CancellationToken.None).ConfigureAwait(false))
        {
            ++_storeCountSinceLastPrune;

            if (shouldDispose)
            {
                // We just disposed the lock; if its pool is now empty, drop the dictionary entry. This alone doesn't
                // guarantee cleanup (a successful acquire leaves an empty lock that lingers until that connection string
                // is used again), so pruning backstops it.
                if (_poolsByConnectionString.TryGetValue(connectionString, out var pool) && pool.Count == 0)
                {
                    _poolsByConnectionString.Remove(connectionString);
                }
            }
            else
            {
                ++_pooledLockCount;

                if (_poolsByConnectionString.TryGetValue(connectionString, out var existing))
                {
                    existing.Enqueue(@lock);
                }
                else
                {
                    var newPool = new Queue<MultiplexedConnectionLock>();
                    newPool.Enqueue(@lock);
                    _poolsByConnectionString.Add(connectionString, newPool);
                }
            }

            if (_IsDueForPruningNoLock())
            {
                await _PrunePoolsNoLockAsync().ConfigureAwait(false);
            }
        }
    }

    private bool _IsDueForPruningNoLock()
    {
        // Pruning is expensive, so amortize it: each store gives one "ticket". The cost to prune is the number of queues
        // plus the total items across them; prune once we've accumulated enough tickets to "pay for" it. Pruning only
        // bounds memory (connection bloat isn't an issue — connections are open only when needed), so we don't even
        // consider it below a storage threshold.
        var pruningCost = _pooledLockCount + _poolsByConnectionString.Count;

        return pruningCost > 64 && _storeCountSinceLastPrune >= pruningCost;
    }

    private async ValueTask _PrunePoolsNoLockAsync()
    {
        _storeCountSinceLastPrune = 0;

        List<string>? connectionStringsToRemove = null;

        foreach (var (connectionString, pool) in _poolsByConnectionString)
        {
            MultiplexedConnectionLock? firstRetainedLock = null;

            while (pool.Count != 0 && pool.Peek() != firstRetainedLock)
            {
                var @lock = pool.Dequeue();

                if (await @lock.GetIsInUseAsync().ConfigureAwait(false))
                {
                    firstRetainedLock ??= @lock;
                    pool.Enqueue(@lock);
                }
                else
                {
                    --_pooledLockCount;
                    await _SuppressDisposeAsync(@lock).ConfigureAwait(false);
                }
            }

            if (pool.Count == 0)
            {
                (connectionStringsToRemove ??= []).Add(connectionString);
            }
        }

        if (connectionStringsToRemove is not null)
        {
            foreach (var connectionString in connectionStringsToRemove)
            {
                _poolsByConnectionString.Remove(connectionString);
            }
        }
    }

    private static async ValueTask _SuppressDisposeAsync(MultiplexedConnectionLock @lock)
    {
        try
        {
            await @lock.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031, ERP022 // Pool teardown must not throw; a failed dispose only loses a connection, never a held lock.
        catch
        {
            // Intentionally empty.
        }
#pragma warning restore CA1031, ERP022
    }
}
