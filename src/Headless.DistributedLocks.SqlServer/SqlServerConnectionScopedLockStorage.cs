// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.SqlServer;

internal sealed class SqlServerConnectionScopedLockStorage : IConnectionScopedLockStorage, IAsyncDisposable
{
    private readonly SqlServerDistributedLockOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, HeldLock> _heldByLeaseId = new(StringComparer.Ordinal);

    // Set at the top of DisposeAsync so an acquire racing teardown does not register a lock the teardown snapshot of
    // _heldByLeaseId already iterated past (which would leak its connection and applock).
    private volatile bool _disposed;

    public SqlServerConnectionScopedLockStorage(
        IOptions<SqlServerDistributedLockOptions> options,
        TimeProvider timeProvider
    )
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public bool BlocksServerSide => false;

#pragma warning disable CA2000 // The acquired connection is transferred to HeldLock, which owns disposal.
    public async ValueTask<ConnectionScopedLockHandle?> TryAcquireAsync(
        string resource,
        string leaseId,
        bool isShared,
        bool observeLoss,
        CancellationToken cancellationToken = default
    )
    {
        var encodedResource = _CreateResource(resource);
        HeldLock? held = null;
        var ownershipTransferred = false;

        try
        {
            var connection = _options.CreateConnection();

            // Build the HeldLock before opening so its StateChange handler and active probe are wired before any
            // command runs: a connection break observed between open and acquire then still cancels the lost token,
            // closing the gap where a clean disconnect could be missed.
            held = new HeldLock(
                resource,
                encodedResource,
                leaseId,
                isShared,
                connection,
                _options.CommandTimeout,
                _timeProvider
            );

            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var acquired = await SqlServerApplicationLock
                .TryAcquireSessionAsync(
                    connection,
                    encodedResource,
                    isShared,
                    TimeSpan.Zero,
                    _options.CommandTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (!acquired)
            {
                return null;
            }

            _heldByLeaseId[leaseId] = held;

            // Close the dispose race: if teardown has begun, the just-acquired lock would be missed by the teardown
            // snapshot, leaking its connection and applock. Drop it explicitly instead of leaving it registered.
            if (_disposed)
            {
                _heldByLeaseId.TryRemove(leaseId, out _);
                ObjectDisposedException.ThrowIf(_disposed, this);
            }

            // Begin active liveness probing only once the lock is held and registered, so a silent half-open
            // connection (no RST, no in-flight command) surfaces as a lost token instead of going unnoticed.
            if (observeLoss)
            {
                held.StartMonitoring();
            }
            ownershipTransferred = true;

            return new ConnectionScopedLockHandle(
                resource,
                leaseId,
                ReleaseAsync,
                observeLoss ? held.ConnectionLostToken : CancellationToken.None
            )
            {
                HeldConnection = held.Connection,
            };
        }
        finally
        {
            if (!ownershipTransferred && held is not null)
            {
                await held.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
#pragma warning restore CA2000

    public async ValueTask ReleaseAsync(
        ConnectionScopedLockHandle handle,
        CancellationToken cancellationToken = default
    )
    {
        await ReleaseAsync(handle.Resource, handle.LeaseId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        if (!_heldByLeaseId.TryRemove(leaseId, out var held))
        {
            return;
        }

        try
        {
            // Serialize against the active liveness probe so two commands never run concurrently on the connection.
            await held.AcquireConnectionGateAsync().ConfigureAwait(false);

            try
            {
                if (held.Connection.State == ConnectionState.Open)
                {
                    await SqlServerApplicationLock
                        .ReleaseSessionAsync(held.Connection, held.EncodedResource, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                held.ReleaseConnectionGate();
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
        var local = _heldByLeaseId.Values.Any(x =>
            string.Equals(x.Resource, resource, StringComparison.Ordinal)
            && (!isShared.HasValue || x.IsShared == isShared.Value)
        );

        return local || await _IsLockedInDatabaseAsync(resource, isShared, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts holders of <paramref name="resource"/>. Local (same-process) holders are counted exactly. Remote
    /// holders are reported as presence only: <c>sp_getapplock</c> exposes the current lock mode via
    /// <c>APPLOCK_TEST</c> but no holder count, so any number of remote shared readers collapses to <c>1</c>.
    /// Callers needing an exact cross-process reader count cannot get it from this backend — unlike the Postgres
    /// provider, which counts <c>pg_locks</c> rows directly. Use this as a held / not-held signal for remote locks.
    /// </summary>
    public async ValueTask<long> GetLocksCountAsync(
        string resource,
        bool? isShared = null,
        CancellationToken cancellationToken = default
    )
    {
        var localCount = (long)
            _heldByLeaseId.Values.Count(x =>
                string.Equals(x.Resource, resource, StringComparison.Ordinal)
                && (!isShared.HasValue || x.IsShared == isShared.Value)
            );

        if (localCount > 0)
        {
            return localCount;
        }

        // Remote holders: APPLOCK_TEST is boolean, so this is presence-only (0 or 1), never an exact remote count.
        return await _IsLockedInDatabaseAsync(resource, isShared, cancellationToken).ConfigureAwait(false) ? 1 : 0;
    }

    public ValueTask<string?> GetLocalLeaseIdAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(
            _heldByLeaseId
                .Values.FirstOrDefault(x => string.Equals(x.Resource, resource, StringComparison.Ordinal))
                ?.LeaseId
        );
    }

    public ValueTask<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DistributedLockInfo> locks = _heldByLeaseId
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

    public ValueTask<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult((long)_heldByLeaseId.Count);
    }

    public async ValueTask DisposeAsync()
    {
        // Set before the snapshot iteration so a concurrent acquire observes disposal and drops its just-acquired
        // lock rather than registering it after the teardown loop has already passed it.
        _disposed = true;

        List<Exception>? teardownErrors = null;

        foreach (var held in _heldByLeaseId.Values)
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

        _heldByLeaseId.Clear();

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
            return !await _CanAcquireAsync(
                    connection,
                    encodedResource,
                    SqlServerApplicationLock.ExclusiveLockMode,
                    cancellationToken
                )
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

        return !await _CanAcquireAsync(
                connection,
                encodedResource,
                SqlServerApplicationLock.SharedLockMode,
                cancellationToken
            )
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
        command.CommandTimeout = SqlServerApplicationLock.GetCommandTimeoutSeconds(
            TimeSpan.Zero,
            _options.CommandTimeout
        );
        command.CommandText = "SELECT APPLOCK_TEST(N'public', @resource, @lockMode, N'Session');";
        command.Parameters.AddWithValue("resource", encodedResource);
        command.Parameters.AddWithValue("lockMode", lockMode);

        return Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture
            ) == 1;
    }

    /// <summary>
    /// A single held session-scoped lock. Owns the dedicated <see cref="SqlConnection"/> and backs the handle's
    /// connection-lost token with two complementary signals: the connection's <c>StateChange</c>
    /// event (clean disconnects) and an active bounded-timeout liveness probe (silent half-open death — a network
    /// drop with no RST where <c>StateChange</c> alone never fires until the next real query). This mirrors the
    /// intent of <c>ConnectionMonitor</c> used by the multiplexing engine providers, which the raw-connection
    /// SqlServer storage cannot reuse directly.
    /// </summary>
    private sealed class HeldLock : IAsyncDisposable
    {
        // Active probe cadence; bounded so a silently-dead connection is detected within roughly this window.
        private static readonly TimeSpan _ProbeCadence = TimeSpan.FromSeconds(30);

        private readonly CancellationTokenSource _lostTokenSource = new();
        private readonly TimeProvider _timeProvider;
        private readonly int _probeCommandTimeoutSeconds;

        // Serializes the active probe against release so two commands never run concurrently on the SqlConnection.
        private readonly SemaphoreSlim _connectionGate = new(1, 1);

        private ITimer? _probeTimer;
        private int _disposed;
        private int _probing;

        public HeldLock(
            string resource,
            string encodedResource,
            string leaseId,
            bool isShared,
            SqlConnection connection,
            TimeSpan commandTimeout,
            TimeProvider timeProvider
        )
        {
            Resource = resource;
            EncodedResource = encodedResource;
            LeaseId = leaseId;
            IsShared = isShared;
            Connection = connection;
            _timeProvider = timeProvider;
            _probeCommandTimeoutSeconds = SqlServerApplicationLock.GetCommandTimeoutSeconds(commandTimeout);

            // Subscribe before the connection opens so a break observed at any point cancels the lost token.
            connection.StateChange += OnStateChanged;
        }

        public string Resource { get; }
        public string EncodedResource { get; }
        public string LeaseId { get; }
        public bool IsShared { get; }
        public SqlConnection Connection { get; }
        public CancellationToken ConnectionLostToken => _lostTokenSource.Token;

        public void StartMonitoring()
        {
            _probeTimer = _timeProvider.CreateTimer(
                static state => ((HeldLock)state!)._ProbeFireAndForget(),
                this,
                _ProbeCadence,
                _ProbeCadence
            );
        }

        public void OnStateChanged(object sender, StateChangeEventArgs args)
        {
            if (args.CurrentState is ConnectionState.Broken or ConnectionState.Closed)
            {
                _lostTokenSource.Cancel();
            }
        }

        private void _ProbeFireAndForget()
        {
            // Skip if a previous probe is still running; the timer fires on a fresh cadence next tick.
            if (Interlocked.CompareExchange(ref _probing, 1, 0) != 0)
            {
                return;
            }

            _ = _ProbeAsync();
        }

        private async Task _ProbeAsync()
        {
            try
            {
                if (Volatile.Read(ref _disposed) != 0 || _lostTokenSource.IsCancellationRequested)
                {
                    return;
                }

                // Zero-wait gate: if release (or another probe) holds the connection, skip this tick rather than
                // queueing a concurrent command on the non-thread-safe SqlConnection.
                if (!await _connectionGate.WaitAsync(TimeSpan.Zero).ConfigureAwait(false))
                {
                    return;
                }

                try
                {
                    if (Connection.State != ConnectionState.Open)
                    {
                        return;
                    }

                    await using var command = Connection.CreateCommand();
                    command.CommandTimeout = _probeCommandTimeoutSeconds;
                    command.CommandText = "SELECT 1 /* Headless distributed-lock connection liveness probe */;";
                    await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _connectionGate.Release();
                }
            }
#pragma warning disable CA1031, ERP022 // Any probe failure means the connection is dead; surface loss via the lost token.
            catch
            {
                await _lostTokenSource.CancelAsync().ConfigureAwait(false);
            }
#pragma warning restore CA1031, ERP022
            finally
            {
                Interlocked.Exchange(ref _probing, 0);
            }
        }

        public async ValueTask AcquireConnectionGateAsync()
        {
            await _connectionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        public void ReleaseConnectionGate()
        {
            _connectionGate.Release();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_probeTimer is not null)
            {
                await _probeTimer.DisposeAsync().ConfigureAwait(false);
            }

            Connection.StateChange -= OnStateChanged;
            await _lostTokenSource.CancelAsync().ConfigureAwait(false);
            _lostTokenSource.Dispose();
            _connectionGate.Dispose();
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
