// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data;
using System.Runtime.ExceptionServices;
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
    private readonly int _commandTimeoutSeconds;

    public PostgresConnectionScopedLockStorage(IOptions<PostgresDistributedLockOptions> options)
    {
        _options = options.Value;
        _dataSource = PostgresDataSourceFactory.CreateDataSource(_options);
        _ownsDataSource = _options.DataSource is null;
        _commandTimeoutSeconds = (int)_options.CommandTimeout.TotalSeconds;
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
            var acquired = await _TryAcquireAsync(connection, key, isShared, _commandTimeoutSeconds, cancellationToken).ConfigureAwait(false);

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

        try
        {
            if (held.Connection.FullState.HasFlag(ConnectionState.Open))
            {
                // Releasing the advisory lock and emitting the wake-up NOTIFY must not be abandoned on
                // caller cancellation; otherwise waiters wait a full polling interval until pool reset.
                await _ReleaseAsync(held.Connection, held.Key, held.IsShared, _commandTimeoutSeconds, CancellationToken.None)
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
        command.CommandTimeout = _commandTimeoutSeconds;
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

    /// <summary>Tears down every held lock and the owned data source.</summary>
    /// <remarks>
    /// A single teardown failure does not strand the remaining locks; all errors are collected and
    /// surfaced after teardown completes. When exactly one error was collected it is rethrown
    /// preserving its original type and stack trace; two or more are wrapped in an
    /// <see cref="AggregateException"/>.
    /// </remarks>
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
                // A single held lock failing to tear down must not strand the remaining locks
                // (which would leak their connections and advisory locks under No Reset On Close).
                // Collect and surface after every lock has had a chance to dispose.
                (teardownErrors ??= []).Add(exception);
            }
        }

        _heldByLockId.Clear();

        if (_ownsDataSource)
        {
            try
            {
                await _dataSource.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Inside the try so a data-source failure does not drop the lock teardown errors.
                (teardownErrors ??= []).Add(exception);
            }
        }

        if (teardownErrors is null)
        {
            return;
        }

        if (teardownErrors.Count == 1)
        {
            // Preserve the original exception type and stack trace; reserve AggregateException for 2+.
            ExceptionDispatchInfo.Capture(teardownErrors[0]).Throw();
        }

        throw new AggregateException(teardownErrors);
    }

    private PostgresAdvisoryLockKey _CreateKey(string resource)
    {
        return PostgresAdvisoryLockKey.FromString(_options.KeyPrefix + resource, allowHashing: true);
    }

    private static async ValueTask<bool> _TryAcquireAsync(
        NpgsqlConnection connection,
        PostgresAdvisoryLockKey key,
        bool isShared,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText = $"SELECT pg_catalog.pg_try_advisory_lock{(isShared ? "_shared" : "")}({AddKeyParameters(command, key)})";

        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }

    private static async ValueTask _ReleaseAsync(
        NpgsqlConnection connection,
        PostgresAdvisoryLockKey key,
        bool isShared,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = commandTimeoutSeconds;
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
        // Keys yields the same (classid, objid) 32-bit split that Postgres stores for the
        // single-bigint encoding (pg_locks splits the bigint into high/low 32-bit halves),
        // so no ToString/FromString round-trip is needed.
        var keys = key.Keys;
        command.Parameters.AddWithValue("classId", keys.Key1);
        command.Parameters.AddWithValue("objId", keys.Key2);

        // pg_locks records objsubid = 1 for single-bigint advisory keys and 2 for the (int,int)
        // form. Filtering on it prevents conflating a single-bigint key with an (int,int) key
        // whose halves coincide.
        command.Parameters.AddWithValue("objSubId", (short)(key.HasSingleKey ? 1 : 2));

        return "l.classid = @classId AND l.objid = @objId AND l.objsubid = @objSubId";
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
        private int _disposed;

        public string Resource { get; } = resource;
        public string LockId { get; } = lockId;
        public PostgresAdvisoryLockKey Key { get; } = key;
        public bool IsShared { get; } = isShared;
        public NpgsqlConnection Connection { get; } = connection;
        public CancellationToken ConnectionLostToken => _lostTokenSource.Token;

        public void OnStateChanged(object sender, StateChangeEventArgs args)
        {
            if (args.CurrentState is ConnectionState.Open)
            {
                return;
            }

            // StateChange fires synchronously on Npgsql's thread and can race disposal: unsubscribe
            // alone cannot abort a handler already in flight. Skip if already disposed and still guard
            // Cancel(), since the disposed flag may be set between the check and the call.
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                _lostTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Disposal won the race after the guard above; the lost token no longer has consumers.
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // Unsubscribe on every teardown path so closing the connection below cannot fire
            // OnStateChanged into an already-disposed CTS. Dispose the connection before the CTS
            // for the same reason (the synchronous StateChange would otherwise hit a dead source).
            Connection.StateChange -= OnStateChanged;
            await Connection.DisposeAsync().ConfigureAwait(false);
            _lostTokenSource.Dispose();
        }
    }
}
#pragma warning restore CA2100
