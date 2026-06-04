// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Implements <see cref="IDbDistributedLock"/> by giving each acquisition a dedicated <see cref="DatabaseConnection"/>
/// (or transaction). Used as the fallback for the optimistic multiplexing lock and as the only path for upgradeable or
/// transaction-scoped strategies.
/// </summary>
internal sealed class DedicatedConnectionOrTransactionDbDistributedLock : IDbDistributedLock
{
    private readonly string _name;
    private readonly Func<DatabaseConnection> _connectionFactory;
    private readonly bool _transactionScopedIfPossible;
    private readonly TimeSpan _keepaliveCadence;

    /// <summary>Constructs an instance using the given EXTERNALLY OWNED connection factory.</summary>
    public DedicatedConnectionOrTransactionDbDistributedLock(
        string name,
        Func<DatabaseConnection> externalConnectionFactory
    )
        // useTransaction is irrelevant for the external-connection flow (it never creates a transaction itself).
        : this(name, externalConnectionFactory, useTransaction: true, keepaliveCadence: Timeout.InfiniteTimeSpan) { }

    public DedicatedConnectionOrTransactionDbDistributedLock(
        string name,
        Func<DatabaseConnection> connectionFactory,
        bool useTransaction,
        TimeSpan keepaliveCadence
    )
    {
        _name = Argument.IsNotNull(name);
        _connectionFactory = Argument.IsNotNull(connectionFactory);
        _transactionScopedIfPossible = useTransaction;
        _keepaliveCadence = keepaliveCadence;
    }

    public async ValueTask<IDistributedLock?> TryAcquireAsync<TLockCookie>(
        TimeSpan timeout,
        IDbSynchronizationStrategy<TLockCookie> strategy,
        IDistributedLock? contextHandle,
        CancellationToken cancellationToken
    )
        where TLockCookie : class
    {
        IDistributedLock? result = null;
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
                    if (_transactionScopedIfPossible)
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
                    transactionScoped: _transactionScopedIfPossible && connection.HasTransaction,
                    connectionResource
                );

                if (_keepaliveCadence != Timeout.InfiniteTimeSpan)
                {
                    connection.SetKeepaliveCadence(_keepaliveCadence);
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

    private static DatabaseConnection _GetContextHandleConnection<TLockCookie>(IDistributedLock contextHandle)
        where TLockCookie : class
    {
        var connection = ((Handle<TLockCookie>)contextHandle).Connection;

        return connection
            ?? throw new ObjectDisposedException(nameof(contextHandle), "The provided handle is already disposed.");
    }

    private sealed class Handle<TLockCookie> : IDistributedLock
        where TLockCookie : class
    {
#pragma warning disable CA2213 // Disposed via Interlocked.Exchange in DisposeAsync (the analyzer cannot see that path).
        private InnerHandle? _innerHandle;
#pragma warning restore CA2213

        public Handle(
            DatabaseConnection connection,
            IDbSynchronizationStrategy<TLockCookie> strategy,
            string name,
            TLockCookie lockCookie,
            bool transactionScoped,
            IAsyncDisposable? connectionResource
        )
        {
            _innerHandle = new InnerHandle(
                connection,
                strategy,
                name,
                lockCookie,
                transactionScoped,
                connectionResource
            );
            LockId = Guid.NewGuid().ToString("N");
            Resource = name;
            DateAcquired = connection.TimeProvider.GetUtcNow();
        }

        public string LockId { get; }

        public long? FencingToken => null;

        public string Resource { get; }

        public int RenewalCount => 0;

        public DateTimeOffset DateAcquired { get; }

        public TimeSpan TimeWaitedForLock => TimeSpan.Zero;

        public bool IsMonitored => true;

        public CancellationToken HandleLostToken =>
            Volatile.Read(ref _innerHandle)?.HandleLostToken
            ?? throw new ObjectDisposedException(nameof(Handle<TLockCookie>));

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
            return Interlocked.Exchange(ref _innerHandle, null)?.DisposeAsync() ?? default;
        }

        private sealed class InnerHandle : IAsyncDisposable
        {
            private static readonly object _DisposedSentinel = new();

            private readonly IDbSynchronizationStrategy<TLockCookie> _strategy;
            private readonly string _name;
            private readonly TLockCookie _lockCookie;
            private readonly bool _transactionScoped;
            private readonly IAsyncDisposable? _connectionResource;
            private object? _connectionMonitoringHandleOrDisposedSentinel;

            public InnerHandle(
                DatabaseConnection connection,
                IDbSynchronizationStrategy<TLockCookie> strategy,
                string name,
                TLockCookie lockCookie,
                bool transactionScoped,
                IAsyncDisposable? connectionResource
            )
            {
                Connection = connection;
                _strategy = strategy;
                _name = name;
                _lockCookie = lockCookie;
                _transactionScoped = transactionScoped;
                _connectionResource = connectionResource;
            }

            public DatabaseConnection Connection { get; }

            public CancellationToken HandleLostToken
            {
                get
                {
                    var existing = Volatile.Read(ref _connectionMonitoringHandleOrDisposedSentinel);

                    if (existing is null)
                    {
                        var newHandle = Connection.GetConnectionMonitoringHandle();
                        existing = Interlocked.CompareExchange(
                            ref _connectionMonitoringHandleOrDisposedSentinel,
                            newHandle,
                            comparand: null
                        );

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
                        !Connection.CanExecuteQueries || (_transactionScoped && !Connection.IsExternallyOwned);

                    if (!canSkipExplicitRelease)
                    {
                        await _strategy.ReleaseAsync(Connection, _name, _lockCookie).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (_connectionResource is not null)
                    {
                        await _connectionResource.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
