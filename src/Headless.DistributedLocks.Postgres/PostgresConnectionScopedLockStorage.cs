// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.Postgres;

#pragma warning disable CA2100 // Advisory SQL text is selected from fixed code paths; lock keys and resources use parameters.
internal sealed class PostgresConnectionScopedLockStorage : IConnectionScopedLockStorage, IAsyncDisposable
{
    private readonly PostgresDistributedLockOptions _options;
    private readonly ConcurrentDictionary<string, HeldLock> _heldByLockId = new(StringComparer.Ordinal);
    private readonly bool _ownsDataSource;
    private readonly NpgsqlDataSource _dataSource;

    public PostgresConnectionScopedLockStorage(IOptions<PostgresDistributedLockOptions> options)
    {
        _options = options.Value;
        _dataSource = _options.DataSource ?? NpgsqlDataSource.Create(_options.ConnectionString!);
        _ownsDataSource = _options.DataSource is null;
    }

    public async ValueTask<ConnectionScopedLockHandle?> TryAcquireAsync(
        string resource,
        string lockId,
        bool isShared,
        CancellationToken cancellationToken = default
    )
    {
        NpgsqlConnection? connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var key = _CreateKey(resource);
        var ownershipTransferred = false;

        try
        {
            var acquired = await _TryAcquireAsync(connection, key, isShared, cancellationToken).ConfigureAwait(false);

            if (!acquired)
            {
                return null;
            }

            var held = new HeldLock(resource, lockId, key, isShared, connection);
            connection.StateChange += held.OnStateChanged;
            _heldByLockId[lockId] = held;
            ownershipTransferred = true;

            return new ConnectionScopedLockHandle(resource, lockId, ReleaseAsync, held.ConnectionLostToken);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

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

        held.Connection.StateChange -= held.OnStateChanged;

        try
        {
            if (held.Connection.FullState.HasFlag(ConnectionState.Open))
            {
                await _ReleaseAsync(held.Connection, held.Key, held.IsShared, cancellationToken).ConfigureAwait(false);
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
        var count = await GetLocksCountAsync(resource, isShared, cancellationToken).ConfigureAwait(false);

        return count > 0;
    }

    public async ValueTask<long> GetLocksCountAsync(
        string resource,
        bool? isShared = null,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var key = _CreateKey(resource);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM pg_catalog.pg_locks l
            JOIN pg_catalog.pg_database d ON d.oid = l.database
            WHERE l.locktype = 'advisory'
              AND l.granted
              AND d.datname = pg_catalog.current_database()
              AND {AddLockFilter(command, key)}
            """;

        if (isShared.HasValue)
        {
            command.CommandText += " AND l.mode = @mode";
            command.Parameters.AddWithValue("mode", isShared.Value ? "ShareLock" : "ExclusiveLock");
        }

        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
    }

    public ValueTask<string?> GetLocalLockIdAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lockId = _heldByLockId.Values.FirstOrDefault(x => string.Equals(x.Resource, resource, StringComparison.Ordinal))?.LockId;

        return ValueTask.FromResult(lockId);
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
        foreach (var held in _heldByLockId.Values)
        {
            await held.DisposeAsync().ConfigureAwait(false);
        }

        _heldByLockId.Clear();

        if (_ownsDataSource)
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
        }
    }

    private PostgresAdvisoryLockKey _CreateKey(string resource)
    {
        return PostgresAdvisoryLockKey.FromString(_options.KeyPrefix + resource, allowHashing: true);
    }

    private static async ValueTask<bool> _TryAcquireAsync(
        NpgsqlConnection connection,
        PostgresAdvisoryLockKey key,
        bool isShared,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT pg_catalog.pg_try_advisory_lock{(isShared ? "_shared" : "")}({AddKeyParameters(command, key)})";

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }

    private static async ValueTask _ReleaseAsync(
        NpgsqlConnection connection,
        PostgresAdvisoryLockKey key,
        bool isShared,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT pg_catalog.pg_advisory_unlock{(isShared ? "_shared" : "")}({AddKeyParameters(command, key)})";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string AddKeyParameters(NpgsqlCommand command, PostgresAdvisoryLockKey key)
    {
        if (key.HasSingleKey)
        {
            command.Parameters.AddWithValue("key", key.Key);
            return "@key";
        }

        var keys = key.Keys;
        command.Parameters.AddWithValue("key1", keys.Key1);
        command.Parameters.AddWithValue("key2", keys.Key2);

        return "@key1, @key2";
    }

    internal static string AddLockFilter(NpgsqlCommand command, PostgresAdvisoryLockKey key)
    {
        var keys = key.HasSingleKey ? PostgresAdvisoryLockKey.FromString(key.ToString()).Keys : key.Keys;
        command.Parameters.AddWithValue("classId", keys.Key1);
        command.Parameters.AddWithValue("objId", keys.Key2);

        return "l.classid = @classId AND l.objid = @objId";
    }

    private sealed class HeldLock(
        string resource,
        string lockId,
        PostgresAdvisoryLockKey key,
        bool isShared,
        NpgsqlConnection connection
    ) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lostTokenSource = new();

        public string Resource { get; } = resource;
        public string LockId { get; } = lockId;
        public PostgresAdvisoryLockKey Key { get; } = key;
        public bool IsShared { get; } = isShared;
        public NpgsqlConnection Connection { get; } = connection;
        public CancellationToken ConnectionLostToken => _lostTokenSource.Token;

        public void OnStateChanged(object sender, StateChangeEventArgs args)
        {
            if (args.CurrentState is not ConnectionState.Open)
            {
                _lostTokenSource.Cancel();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _lostTokenSource.Dispose();
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
#pragma warning restore CA2100
