// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.Postgres;

#pragma warning disable CA2100 // Advisory SQL text is selected from fixed code paths; lock keys and resources use parameters.

/// <summary>
/// Connection-scoped advisory-lock storage that drives acquisition through the multiplexing engine
/// (<see cref="OptimisticConnectionMultiplexingDbDistributedLock"/>). The engine decides share-vs-dedicate per
/// acquisition and, on a dedicated connection, attaches a <see cref="ConnectionMonitor"/> whose active probe backs the
/// handle's connection-lost token. This adapter keeps the observability queries (<c>pg_locks</c> counts/listing) on the
/// owned <see cref="NpgsqlDataSource"/> directly, and bridges the engine handle's
/// <see cref="IDistributedLease.LostToken"/> onto <see cref="ConnectionScopedLockHandle.ConnectionLostToken"/>.
/// </summary>
internal sealed class PostgresConnectionScopedLockStorage : IConnectionScopedLockStorage, IAsyncDisposable
{
    private readonly PostgresDistributedLockOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, HeldLock> _heldByLockId = new(StringComparer.Ordinal);
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;
    private readonly TimeSpan _keepaliveCadence;
    private readonly MultiplexedConnectionLockPool _multiplexedConnectionLockPool;
    private readonly PostgresAdvisoryLock _exclusiveLock;
    private readonly PostgresAdvisoryLock _sharedLock;

    // The engine wrapper only varies by resource name (pool + connection string are storage-level
    // constants), so it is cached per resolved name and reused across acquisitions.
    private readonly ConcurrentDictionary<string, OptimisticConnectionMultiplexingDbDistributedLock> _enginesByName =
        new(StringComparer.Ordinal);

    // Set at the top of DisposeAsync so an acquire racing teardown does not leave a lock that the
    // teardown loop already missed (it iterates a snapshot of _heldByLockId).
    private volatile bool _disposed;

    /// <summary>
    /// Initializes the connection-scoped lock storage over the shared Npgsql data source, wiring the
    /// advisory-lock engines, multiplexed connection pool, and keepalive cadence from the resolved options.
    /// </summary>
    /// <param name="options">Resolved provider options (polling fallback, command timeout, keepalive, key prefix).</param>
    /// <param name="dataSource">
    /// The shared <see cref="NpgsqlDataSource"/> injected by <see cref="PostgresLockDataSource"/>. Not
    /// disposed by this instance; disposal is owned by the DI registration.
    /// </param>
    /// <param name="timeProvider">Time source used for monitoring sleeps and keepalive cadence.</param>
    public PostgresConnectionScopedLockStorage(
        IOptions<PostgresDistributedLockOptions> options,
        NpgsqlDataSource dataSource,
        TimeProvider timeProvider
    )
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        // The data source is owned and disposed by the DI registration (a single shared instance across the
        // three Postgres consumers), so this storage never disposes it.
        _dataSource = dataSource;
        _connectionString = _dataSource.ConnectionString;
        _commandTimeoutSeconds = (int)_options.CommandTimeout.TotalSeconds;
        _exclusiveLock = new PostgresAdvisoryLock(isShared: false, timeProvider);
        _sharedLock = new PostgresAdvisoryLock(isShared: true, timeProvider);

        // The ConnectionMonitor's keepalive probe is complementary to TCP keepalive (configured on the data source):
        // a positive interval keeps an idle holder's connection warm so a provider idle-timeout cannot silently drop
        // it. Zero leaves keepalive to the monitoring/StateChange paths only.
        _keepaliveCadence = _options.KeepAlive > TimeSpan.Zero ? _options.KeepAlive : Timeout.InfiniteTimeSpan;

        // The factory ignores the engine's per-acquire connection-string argument: every acquisition for this storage
        // uses the one owned data source (which already carries the resolved connection string used as the pool key).
        _multiplexedConnectionLockPool = new MultiplexedConnectionLockPool(_CreateConnection);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Internally uses <c>pg_try_advisory_lock</c> (non-blocking) via the multiplexing engine, which
    /// returns <see langword="null"/> immediately when the resource is held in a conflicting mode.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when <see cref="DisposeAsync"/> has already been called and the just-acquired engine
    /// handle is dropped to avoid leaking it.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the engine returns.
    /// </exception>
    public async ValueTask<ConnectionScopedLockHandle?> TryAcquireAsync(
        string resource,
        string leaseId,
        bool isShared,
        bool observeLoss,
        CancellationToken cancellationToken = default
    )
    {
        var name = _options.KeyPrefix + resource;
        var strategy = isShared ? _sharedLock : _exclusiveLock;

        var engine = _enginesByName.GetOrAdd(
            name,
            static (key, state) =>
                new OptimisticConnectionMultiplexingDbDistributedLock(
                    key,
                    state._connectionString,
                    state._multiplexedConnectionLockPool,
                    state._keepaliveCadence
                ),
            this
        );

        // A zero timeout maps to a single non-blocking try (pg_try_advisory_lock): the provider owns the wait/retry
        // loop, so the engine must never block here.
        var engineHandle = await engine
            .TryAcquireAsync(TimeSpan.Zero, strategy, contextHandle: null, cancellationToken)
            .ConfigureAwait(false);

        if (engineHandle is null)
        {
            return null;
        }

        var ownershipTransferred = false;

        try
        {
            var connectionLostToken = observeLoss ? engineHandle.LostToken : CancellationToken.None;

            // Close the dispose race: if teardown has begun, the just-acquired lock would be missed by the
            // teardown snapshot of _heldByLockId, leaking its connection and advisory lock under No Reset On
            // Close. Drop it explicitly instead of registering it.
            ObjectDisposedException.ThrowIf(_disposed, this);

            var held = new HeldLock(resource, leaseId, engineHandle);
            _heldByLockId[leaseId] = held;
            ownershipTransferred = true;

            return new ConnectionScopedLockHandle(resource, leaseId, ReleaseAsync, connectionLostToken);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                await engineHandle.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask ReleaseAsync(
        ConnectionScopedLockHandle handle,
        CancellationToken cancellationToken = default
    )
    {
        await ReleaseAsync(handle.Resource, handle.LeaseId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// If no lock with the given <paramref name="leaseId"/> is registered, the call is a no-op. The
    /// engine handle is disposed with a non-cancellable release path so the advisory lock and
    /// connection are always returned to the pool regardless of caller cancellation.
    /// </remarks>
    public async ValueTask ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        if (!_heldByLockId.TryRemove(leaseId, out var held))
        {
            return;
        }

        // Disposing the engine handle releases the advisory lock and the monitoring registration, then returns the
        // connection to the multiplexing pool (or tears it down on the dedicated path). This must not be abandoned on
        // caller cancellation, so the engine release path uses its own non-cancellable unlock internally.
        await held.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Queries <c>pg_catalog.pg_locks</c> on a freshly-opened connection from the owned data source.
    /// Returns <see langword="true"/> when the granted-lock count for the resource key exceeds zero.
    /// Underlying Npgsql errors propagate to the caller.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the query completes.
    /// </exception>
    public async ValueTask<bool> IsLockedAsync(
        string resource,
        bool? isShared = null,
        CancellationToken cancellationToken = default
    )
    {
        var count = await GetLocksCountAsync(resource, isShared, cancellationToken).ConfigureAwait(false);

        return count > 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Queries <c>pg_catalog.pg_locks</c> for the resource key across the current database, optionally
    /// filtered by lock mode (<c>ShareLock</c> or <c>ExclusiveLock</c>). Underlying Npgsql errors
    /// propagate to the caller.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the query completes.
    /// </exception>
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
              AND {key.AddLockFilter(command)}
            """;

        if (isShared.HasValue)
        {
            command.CommandText += " AND l.mode = @mode";
            command.Parameters.AddWithValue("mode", isShared.Value ? "ShareLock" : "ExclusiveLock");
        }

        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The lookup is local-only: it scans the in-process <c>_heldByLockId</c> dictionary and does not
    /// query the database. Returns <see langword="null"/> when this process does not hold the resource.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is already cancelled on entry (checked
    /// synchronously via <see cref="CancellationToken.ThrowIfCancellationRequested"/>).
    /// </exception>
    public ValueTask<string?> GetLocalLeaseIdAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var leaseId = _heldByLockId
            .Values.FirstOrDefault(x => string.Equals(x.Resource, resource, StringComparison.Ordinal))
            ?.LeaseId;

        return ValueTask.FromResult(leaseId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns only the locks held by this process. Because long resource names are hashed into advisory
    /// integer keys, <c>pg_locks</c> cannot reverse-map them to the original resource name; a
    /// database-wide listing is therefore not possible and the result is limited to the local
    /// in-process tracking dictionary. <see cref="DistributedLockInfo.TimeToLive"/> and
    /// <see cref="DistributedLockInfo.FencingToken"/> are always <see langword="null"/> for advisory locks.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is already cancelled on entry.
    /// </exception>
    public ValueTask<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // pg_locks can answer "is resource X locked?" because the caller supplies the resource key, but it cannot
        // enumerate this provider's resource names across the whole namespace once long keys are hashed into advisory
        // integers. Provider-wide listing therefore remains local-handle only.
        IReadOnlyList<DistributedLockInfo> locks = _heldByLockId
            .Values.Select(x => new DistributedLockInfo
            {
                Resource = x.Resource,
                LeaseId = x.LeaseId,
                TimeToLive = null,
                FencingToken = null,
            })
            .ToList();

        return ValueTask.FromResult(locks);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the in-process count of currently tracked lock handles. Does not query the database.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is already cancelled on entry.
    /// </exception>
    public ValueTask<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult((long)_heldByLockId.Count);
    }

    /// <summary>Tears down every held advisory lock. The shared data source is not disposed here.</summary>
    /// <remarks>
    /// A single teardown failure does not strand the remaining locks; all errors are collected and
    /// surfaced after teardown completes. When exactly one error was collected it is rethrown
    /// preserving its original type and stack trace; two or more are wrapped in an
    /// <see cref="AggregateException"/>.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        // Set before the snapshot iteration so a concurrent acquire observes disposal and drops its
        // just-acquired lock rather than registering it after the teardown loop has already passed.
        _disposed = true;

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

        // The data source is shared and owned by the DI registration; it is not disposed here.

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

    private DatabaseConnection _CreateConnection(string connectionString)
    {
        // connectionString is the engine's pool key; the storage always opens against its owned data source so an
        // injected DataSource (with its configuration and pooling) is honored.
        return new PostgresDatabaseConnection(_dataSource, _timeProvider, _commandTimeoutSeconds);
    }

    private PostgresAdvisoryLockKey _CreateKey(string resource)
    {
        return PostgresAdvisoryLockKey.FromString(_options.KeyPrefix + resource, allowHashing: true);
    }

    /// <summary>
    /// A single held lock: owns the engine handle (which owns the connection lifecycle, the advisory unlock, and the
    /// connection-monitoring registration). Disposal is idempotent and delegates entirely to the engine handle.
    /// </summary>
    private sealed class HeldLock(string resource, string leaseId, IDistributedLease engineHandle) : IAsyncDisposable
    {
        private int _disposed;

        public string Resource { get; } = resource;

        public string LeaseId { get; } = leaseId;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            await engineHandle.DisposeAsync().ConfigureAwait(false);
        }
    }
}
#pragma warning restore CA2100
