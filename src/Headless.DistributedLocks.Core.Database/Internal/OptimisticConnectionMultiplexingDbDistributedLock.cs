// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Implements <see cref="IDbDistributedLock"/> by multiplexing many advisory locks onto pooled connections where
/// possible, falling back to a dedicated connection when multiplexing is not applicable (upgradeable strategy, a
/// context handle for nested acquire, or contention on the shared connection).
/// </summary>
internal sealed class OptimisticConnectionMultiplexingDbDistributedLock : IDbDistributedLock
{
    private readonly string _name;
    private readonly string _connectionString;
    private readonly MultiplexedConnectionLockPool _multiplexedConnectionLockPool;
    private readonly TimeSpan _keepaliveCadence;
    private readonly IDbDistributedLock _fallbackLock;

    /// <summary>
    /// Initializes an optimistic multiplexing lock.
    /// </summary>
    /// <param name="name">The lock resource name.</param>
    /// <param name="connectionString">The connection string used to key the pool and open fallback connections.</param>
    /// <param name="multiplexedConnectionLockPool">The shared pool of multiplexed connections for this connection string.</param>
    /// <param name="keepaliveCadence">Keepalive interval forwarded to both the pool path and the dedicated fallback.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/>, <paramref name="connectionString"/>, or <paramref name="multiplexedConnectionLockPool"/> is <see langword="null"/>.</exception>
    public OptimisticConnectionMultiplexingDbDistributedLock(
        string name,
        string connectionString,
        MultiplexedConnectionLockPool multiplexedConnectionLockPool,
        TimeSpan keepaliveCadence
    )
    {
        _name = Argument.IsNotNull(name);
        _connectionString = Argument.IsNotNull(connectionString);
        _multiplexedConnectionLockPool = Argument.IsNotNull(multiplexedConnectionLockPool);
        _keepaliveCadence = keepaliveCadence;
        _fallbackLock = new DedicatedConnectionOrTransactionDbDistributedLock(
            _name,
            () => _multiplexedConnectionLockPool.ConnectionFactory(_connectionString),
            useTransaction: false,
            keepaliveCadence: keepaliveCadence
        );
    }

    /// <summary>
    /// Attempts to acquire the lock. Upgradeable strategies and nested acquires (non-null
    /// <paramref name="contextHandle"/>) are routed directly to the dedicated-connection fallback.
    /// All other acquires go through the <see cref="MultiplexedConnectionLockPool"/>.
    /// </summary>
    /// <typeparam name="TLockCookie">The strategy's opaque acquire/release state.</typeparam>
    /// <param name="timeout">The maximum wait time for the strategy acquire.</param>
    /// <param name="strategy">The SQL synchronization strategy.</param>
    /// <param name="contextHandle">
    /// When non-<see langword="null"/>, a nested acquire that must reuse the outer handle's connection;
    /// routes to the dedicated fallback.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the acquire attempt.</param>
    /// <returns>An <see cref="IDistributedLease"/> on success, or <see langword="null"/> on failure.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public ValueTask<IDistributedLease?> TryAcquireAsync<TLockCookie>(
        TimeSpan timeout,
        IDbSynchronizationStrategy<TLockCookie> strategy,
        IDistributedLease? contextHandle,
        CancellationToken cancellationToken
    )
        where TLockCookie : class
    {
        // We cannot multiplex upgradeable locks (an elevation request may block for a long time on the shared
        // connection, holding up other locks' release) or nested acquires (the context handle pins a specific
        // connection). Both route to the dedicated fallback.
        if (!strategy.IsUpgradeable && contextHandle is null)
        {
            return _multiplexedConnectionLockPool.TryAcquireAsync(
                _connectionString,
                _name,
                timeout,
                strategy,
                _keepaliveCadence,
                cancellationToken
            );
        }

        return _fallbackLock.TryAcquireAsync(timeout, strategy, contextHandle, cancellationToken);
    }
}
