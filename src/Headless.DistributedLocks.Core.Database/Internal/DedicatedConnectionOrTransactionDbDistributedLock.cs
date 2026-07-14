// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Implements <see cref="IDbDistributedLock"/> by giving each acquisition a dedicated <see cref="DatabaseConnection"/>
/// (or transaction). Used as the fallback for the optimistic multiplexing lock and as the only path for upgradeable or
/// transaction-scoped strategies.
/// </summary>
/// <remarks>
/// Constructs an instance with full control over transaction scope and keepalive.
/// </remarks>
/// <param name="name">The lock name / resource.</param>
/// <param name="connectionFactory">Factory that creates or returns a <see cref="DatabaseConnection"/>.</param>
/// <param name="useTransaction">When <see langword="true"/>, begins a transaction on internally-owned connections and uses it to scope the advisory lock.</param>
/// <param name="keepaliveCadence">Keepalive ping interval for held connections; <see cref="Timeout.InfiniteTimeSpan"/> disables keepalive.</param>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> or <paramref name="connectionFactory"/> is <see langword="null"/>.</exception>
internal sealed class DedicatedConnectionOrTransactionDbDistributedLock(
    string name,
    Func<DatabaseConnection> connectionFactory,
    bool useTransaction,
    TimeSpan keepaliveCadence
) : IDbDistributedLock
{
    private readonly string _name = Argument.IsNotNull(name);
    private readonly Func<DatabaseConnection> _connectionFactory = Argument.IsNotNull(connectionFactory);

    /// <summary>
    /// Constructs an instance that wraps a factory returning an externally-owned connection. The connection
    /// is never opened, closed, or had a transaction started by this lock.
    /// </summary>
    /// <param name="name">The lock name / resource.</param>
    /// <param name="externalConnectionFactory">Factory that returns an already-open, externally-owned connection.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> or <paramref name="externalConnectionFactory"/> is <see langword="null"/>.</exception>
    public DedicatedConnectionOrTransactionDbDistributedLock(
        string name,
        Func<DatabaseConnection> externalConnectionFactory
    )
        // useTransaction is irrelevant for the external-connection flow (it never creates a transaction itself).
        : this(name, externalConnectionFactory, useTransaction: true, keepaliveCadence: Timeout.InfiniteTimeSpan) { }

    /// <summary>
    /// Attempts to acquire the lock by opening a dedicated connection (or reusing the externally-owned one)
    /// and invoking the strategy. Returns <see langword="null"/> if the strategy reports failure; returns a
    /// live <see cref="IDistributedLease"/> handle on success.
    /// </summary>
    /// <typeparam name="TLockCookie">The strategy's opaque acquire/release state.</typeparam>
    /// <param name="timeout">The maximum wait time passed to the strategy.</param>
    /// <param name="strategy">The SQL synchronization strategy that emits the acquire/release SQL.</param>
    /// <param name="contextHandle">
    /// When non-<see langword="null"/>, a nested acquire: the lock reuses the connection from this existing
    /// handle rather than opening a new one.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the connection open and strategy acquire.</param>
    /// <returns>An <see cref="IDistributedLease"/> on success, or <see langword="null"/> on failure.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the externally-owned connection is disposed or closed, or when <paramref name="contextHandle"/>
    /// has already been disposed.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown when <paramref name="contextHandle"/> is already disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled during connection open or strategy acquire.</exception>
    public async ValueTask<IDistributedLease?> TryAcquireAsync<TLockCookie>(
        TimeSpan timeout,
        IDbSynchronizationStrategy<TLockCookie> strategy,
        IDistributedLease? contextHandle,
        CancellationToken cancellationToken
    )
        where TLockCookie : class
    {
        IDistributedLease? result = null;
        IAsyncDisposable? connectionResource = null;

        try
        {
            DatabaseConnection connection;

            if (contextHandle is not null)
            {
                connection = _GetContextHandleConnection<TLockCookie>(contextHandle);
            }
            else
            {
                connectionResource = connection = _connectionFactory();

                if (connection.IsExternallyOwned)
                {
                    if (!connection.CanExecuteQueries)
                    {
                        throw new InvalidOperationException(
                            "The connection and/or transaction are disposed or closed."
                        );
                    }
                }
                else
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                    // For an internally-owned connection, we must create the transaction ourselves.
                    if (useTransaction)
                    {
                        await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            var lockCookie = await strategy
                .TryAcquireAsync(connection, _name, timeout, cancellationToken)
                .ConfigureAwait(false);

            if (lockCookie is not null)
            {
                result = new Handle<TLockCookie>(
                    connection,
                    strategy,
                    _name,
                    lockCookie,
                    transactionScoped: useTransaction && connection.HasTransaction,
                    connectionResource
                );

                if (keepaliveCadence != Timeout.InfiniteTimeSpan)
                {
                    connection.SetKeepaliveCadence(keepaliveCadence);
                }
            }
        }
        finally
        {
            // If we failed to acquire (or threw), clean up the connection we opened.
            if (result is null && connectionResource is not null)
            {
                await connectionResource.DisposeAsync().ConfigureAwait(false);
            }
        }

        return result;
    }

    private static DatabaseConnection _GetContextHandleConnection<TLockCookie>(IDistributedLease contextHandle)
        where TLockCookie : class
    {
        var connection = ((Handle<TLockCookie>)contextHandle).Connection;

        return connection
            ?? throw new ObjectDisposedException(nameof(contextHandle), "The provided handle is already disposed.");
    }

    private sealed class Handle<TLockCookie>(
        DatabaseConnection connection,
        IDbSynchronizationStrategy<TLockCookie> strategy,
        string name,
        TLockCookie lockCookie,
        bool transactionScoped,
        IAsyncDisposable? connectionResource
    ) : IDistributedLease
        where TLockCookie : class
    {
#pragma warning disable CA2213 // Disposed via Interlocked.Exchange in DisposeAsync (the analyzer cannot see that path).
        private InnerHandle? _innerHandle = new(
            connection,
            strategy,
            name,
            lockCookie,
            transactionScoped,
            connectionResource
        );

#pragma warning restore CA2213

        public string LeaseId { get; } = Guid.NewGuid().ToString("N");

        public long? FencingToken => null;

        public string Resource { get; } = name;

        public int RenewalCount => 0;

        public DateTimeOffset DateAcquired { get; } = connection.TimeProvider.GetUtcNow();

        public TimeSpan TimeWaitedForLock => TimeSpan.Zero;

        public bool CanObserveLoss => true;

        public CancellationToken LostToken =>
            Volatile.Read(ref _innerHandle)?.LostToken ?? throw new ObjectDisposedException(nameof(Handle<>));

        public DatabaseConnection? Connection => Volatile.Read(ref _innerHandle)?.Connection;

        public async Task ReleaseAsync() => await DisposeAsync().ConfigureAwait(false);

        public Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
        {
            // The advisory lock is held for the connection's/transaction's lifetime; there is no lease to renew.
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            return Interlocked.Exchange(ref _innerHandle, value: null)?.DisposeAsync() ?? default;
        }

        private sealed class InnerHandle(
            DatabaseConnection connection,
            IDbSynchronizationStrategy<TLockCookie> strategy,
            string name,
            TLockCookie lockCookie,
            bool transactionScoped,
            IAsyncDisposable? connectionResource
        ) : IAsyncDisposable
        {
            private static readonly object _DisposedSentinel = new();

            private object? _connectionMonitoringHandleOrDisposedSentinel;

            public DatabaseConnection Connection { get; } = connection;

            public CancellationToken LostToken
            {
                get
                {
                    var existing = Volatile.Read(ref _connectionMonitoringHandleOrDisposedSentinel);

                    if (existing is null)
                    {
                        var newHandle = Connection.GetConnectionMonitoringHandle();
#pragma warning disable MA0173 // LazyInitializer.EnsureInitialized cannot dispose the losing racer's IDisposable handle nor model the tri-state disposed sentinel; the hand-rolled CAS is required.
                        existing = Interlocked.CompareExchange(
                            ref _connectionMonitoringHandleOrDisposedSentinel,
                            newHandle,
                            comparand: null
                        );
#pragma warning restore MA0173

                        if (existing is null)
                        {
                            // We won the race; use our new handle.
                            return newHandle.ConnectionLostToken;
                        }

                        // We lost the race; discard our handle. existing is now either a racing handle or the sentinel.
                        newHandle.Dispose();
                    }

                    ObjectDisposedException.ThrowIf(ReferenceEquals(existing, _DisposedSentinel), this);

                    return ((IDatabaseConnectionMonitoringHandle)existing).ConnectionLostToken;
                }
            }

            public async ValueTask DisposeAsync()
            {
                var connectionMonitoringHandleOrDisposedSentinel = Interlocked.Exchange(
                    ref _connectionMonitoringHandleOrDisposedSentinel,
                    _DisposedSentinel
                );

                if (ReferenceEquals(connectionMonitoringHandleOrDisposedSentinel, _DisposedSentinel))
                {
                    return;
                }

                if (connectionMonitoringHandleOrDisposedSentinel is IDatabaseConnectionMonitoringHandle handle)
                {
                    handle.Dispose();
                }

                try
                {
                    // For transaction-scoped locks we can skip the explicit release when either (a) we own the
                    // connection and therefore the transaction (disposing the transaction releases the lock), or (b) the
                    // transaction is dead (completed/rolled back) so the lock has already released server-side. For any
                    // scope, a connection that can no longer execute queries has already dropped its advisory locks
                    // server-side, so the explicit unlock is unnecessary and would only fault.
                    var canSkipExplicitRelease =
                        !Connection.CanExecuteQueries || (transactionScoped && !Connection.IsExternallyOwned);

                    if (!canSkipExplicitRelease)
                    {
                        await strategy.ReleaseAsync(Connection, name, lockCookie).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (connectionResource is not null)
                    {
                        await connectionResource.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
