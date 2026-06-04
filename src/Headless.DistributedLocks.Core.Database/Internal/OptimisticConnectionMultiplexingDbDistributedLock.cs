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

    public ValueTask<IDistributedLock?> TryAcquireAsync<TLockCookie>(
        TimeSpan timeout,
        IDbSynchronizationStrategy<TLockCookie> strategy,
        IDistributedLock? contextHandle,
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
