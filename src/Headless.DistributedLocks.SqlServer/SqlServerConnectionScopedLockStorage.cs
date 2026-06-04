// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using Headless.DistributedLocks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.SqlServer;

internal sealed class SqlServerConnectionScopedLockStorage : IConnectionScopedLockStorage, IAsyncDisposable
{
    private readonly SqlServerDistributedLockOptions _options;
    private readonly ConcurrentDictionary<string, HeldLock> _heldByLockId = new(StringComparer.Ordinal);

    public SqlServerConnectionScopedLockStorage(IOptions<SqlServerDistributedLockOptions> options)
    {
        _options = options.Value;
    }

    public bool BlocksServerSide => true;

#pragma warning disable CA2000 // The acquired connection is transferred to HeldLock, which owns disposal.
    public async ValueTask<ConnectionScopedLockHandle?> TryAcquireAsync(
        string resource,
        string lockId,
        bool isShared,
        TimeSpan acquireTimeout,
        CancellationToken cancellationToken = default
    )
    {
        SqlConnection? connection = null;
        var encodedResource = _CreateResource(resource);
        var ownershipTransferred = false;

        try
        {
            connection = _options.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var acquired = await SqlServerApplicationLock
                .TryAcquireSessionAsync(connection, encodedResource, isShared, acquireTimeout, _options.CommandTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (!acquired)
            {
                return null;
            }

            var held = new HeldLock(resource, encodedResource, lockId, isShared, connection);
            connection.StateChange += held.OnStateChanged;
            _heldByLockId[lockId] = held;
            ownershipTransferred = true;
            connection = null;

            return new ConnectionScopedLockHandle(resource, lockId, ReleaseAsync, held.ConnectionLostToken);
        }
        finally
        {
            if (!ownershipTransferred && connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
#pragma warning restore CA2000

    public async ValueTask ReleaseAsync(ConnectionScopedLockHandle handle, CancellationToken cancellationToken = default)
    {
        await ReleaseAsync(handle.Resource, handle.LockId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        if (!_heldByLockId.TryRemove(lockId, out var held))
        {
            return;
        }

        try
        {
            if (held.Connection.State == ConnectionState.Open)
            {
                await SqlServerApplicationLock.ReleaseSessionAsync(held.Connection, held.EncodedResource, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            await held.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<bool> IsLockedAsync(
        string resource,
        bool? isShared = null,
        CancellationToken cancellationToken = default
    )
    {
        var local = _heldByLockId.Values.Any(x =>
            string.Equals(x.Resource, resource, StringComparison.Ordinal)
            && (!isShared.HasValue || x.IsShared == isShared.Value)
        );

        return local || await _IsLockedInDatabaseAsync(resource, isShared, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<long> GetLocksCountAsync(
        string resource,
        bool? isShared = null,
        CancellationToken cancellationToken = default
    )
    {
        var localCount = (long)_heldByLockId.Values.Count(x =>
            string.Equals(x.Resource, resource, StringComparison.Ordinal)
            && (!isShared.HasValue || x.IsShared == isShared.Value)
        );

        if (localCount > 0)
        {
            return localCount;
        }

        return await _IsLockedInDatabaseAsync(resource, isShared, cancellationToken).ConfigureAwait(false) ? 1 : 0;
    }

    public ValueTask<string?> GetLocalLockIdAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(
            _heldByLockId.Values.FirstOrDefault(x => string.Equals(x.Resource, resource, StringComparison.Ordinal))?.LockId
        );
    }

    public ValueTask<IReadOnlyList<LockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<LockInfo> locks = _heldByLockId
            .Values.Select(x => new LockInfo
            {
                Resource = x.Resource,
                LockId = x.LockId,
                TimeToLive = null,
                FencingToken = null,
            })
            .ToList();

        return ValueTask.FromResult(locks);
    }

    public ValueTask<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult((long)_heldByLockId.Count);
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception>? teardownErrors = null;

        foreach (var held in _heldByLockId.Values)
        {
            try
            {
                await held.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (teardownErrors ??= []).Add(exception);
            }
        }

        _heldByLockId.Clear();

        if (teardownErrors is not null)
        {
            throw new AggregateException(teardownErrors);
        }
    }

    private string _CreateResource(string resource) => SqlServerResourceName.Encode(_options.KeyPrefix + resource);

    private async ValueTask<bool> _IsLockedInDatabaseAsync(
        string resource,
        bool? isShared,
        CancellationToken cancellationToken
    )
    {
        await using var connection = _options.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var encodedResource = _CreateResource(resource);

        if (isShared is null)
        {
            return !await _CanAcquireAsync(connection, encodedResource, SqlServerApplicationLock.ExclusiveLockMode, cancellationToken)
                .ConfigureAwait(false);
        }

        if (isShared.Value)
        {
            var canAcquireShared = await _CanAcquireAsync(
                    connection,
                    encodedResource,
                    SqlServerApplicationLock.SharedLockMode,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (!canAcquireShared)
            {
                return false;
            }

            return !await _CanAcquireAsync(
                    connection,
                    encodedResource,
                    SqlServerApplicationLock.ExclusiveLockMode,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return !await _CanAcquireAsync(connection, encodedResource, SqlServerApplicationLock.SharedLockMode, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<bool> _CanAcquireAsync(
        SqlConnection connection,
        string encodedResource,
        string lockMode,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = SqlServerApplicationLock.GetCommandTimeoutSeconds(TimeSpan.Zero, _options.CommandTimeout);
        command.CommandText = "SELECT APPLOCK_TEST(N'public', @resource, @lockMode, N'Session');";
        command.Parameters.AddWithValue("resource", encodedResource);
        command.Parameters.AddWithValue("lockMode", lockMode);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture)
            == 1;
    }

    private sealed class HeldLock(
        string resource,
        string encodedResource,
        string lockId,
        bool isShared,
        SqlConnection connection
    ) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lostTokenSource = new();
        private int _disposed;

        public string Resource { get; } = resource;
        public string EncodedResource { get; } = encodedResource;
        public string LockId { get; } = lockId;
        public bool IsShared { get; } = isShared;
        public SqlConnection Connection { get; } = connection;
        public CancellationToken ConnectionLostToken => _lostTokenSource.Token;

        public void OnStateChanged(object sender, StateChangeEventArgs args)
        {
            if (args.CurrentState is ConnectionState.Broken or ConnectionState.Closed)
            {
                _lostTokenSource.Cancel();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Connection.StateChange -= OnStateChanged;
            await _lostTokenSource.CancelAsync().ConfigureAwait(false);
            _lostTokenSource.Dispose();
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
