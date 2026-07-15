// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Allows multiple advisory locks to be taken on a single <see cref="DatabaseConnection"/> (optimistic multiplexing).
/// Thread-safe except for <see cref="DisposeAsync"/>, which must be called only when no locks are held.
/// </summary>
/// <remarks>
/// The held-lock set is keyed by the strategy's resolved physical-lock identity (see
/// <see cref="IDbSynchronizationStrategy{TLockCookie}.GetHeldLockIdentity"/>), not the resource string, so two distinct
/// resource strings that map to the same advisory key cannot both believe they hold an exclusive lock on one shared
/// connection — the colliding acquirer is reported as "already held" and routed to a dedicated connection.
/// </remarks>
/// <remarks>
/// Initializes a multiplexed lock over <paramref name="connection"/>. The connection must not
/// yet be open; <see cref="TryAcquireAsync{TLockCookie}"/> opens it on the first successful acquire.
/// </remarks>
/// <param name="connection">The database connection to multiplex advisory locks on.</param>
internal sealed class MultiplexedConnectionLock(DatabaseConnection connection) : IAsyncDisposable
{
    // Limits concurrent use of the connection to one acquire/release at a time. SemaphoreSlim (not Nito.AsyncEx.AsyncLock)
    // because the opportunistic path needs a zero-wait try-acquire, which AsyncLock does not expose.
#pragma warning disable CA2213 // Disposed at the end of DisposeAsync, after the connection is torn down.
    private readonly SemaphoreSlim _mutex = new(initialCount: 1, maxCount: 1);
#pragma warning restore CA2213

    private readonly Dictionary<object, TimeSpan> _heldLockIdentitiesToKeepaliveCadences = [];
    private readonly DatabaseConnection _connection = connection;

    // We track this explicitly (rather than reading DatabaseConnection.CanExecuteQueries) so we Close() once per Open()
    // and never try to re-open a broken connection.
    private bool _connectionOpened;

    private bool IsConnectionBrokenNoLock => _connectionOpened && !_connection.CanExecuteQueries;

    /// <summary>
    /// Attempts to acquire an advisory lock for <paramref name="name"/> on the shared connection.
    /// </summary>
    /// <typeparam name="TLockCookie">The strategy's opaque acquire/release state.</typeparam>
    /// <param name="name">The lock resource name.</param>
    /// <param name="timeout">The acquire timeout passed to the strategy (ignored when <paramref name="opportunistic"/> is <see langword="true"/> — a zero timeout is used instead).</param>
    /// <param name="strategy">The SQL synchronization strategy.</param>
    /// <param name="keepaliveCadence">The keepalive interval to track alongside this lock on the shared connection.</param>
    /// <param name="opportunistic">
    /// When <see langword="true"/>, the method uses a zero mutex-wait and a zero strategy timeout to avoid
    /// blocking on the shared connection; a non-acquired result carries a retry recommendation.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel connection open and strategy acquire.</param>
    /// <returns>A <see cref="Result"/> carrying either a live handle or a retry/no-retry recommendation.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled while waiting for the mutex or during the strategy acquire.</exception>
    public async ValueTask<Result> TryAcquireAsync<TLockCookie>(
        string name,
        TimeSpan timeout,
        IDbSynchronizationStrategy<TLockCookie> strategy,
        TimeSpan keepaliveCadence,
        bool opportunistic,
        CancellationToken cancellationToken
    )
        where TLockCookie : class
    {
        var mutexAcquired = await _mutex
            .WaitAsync(opportunistic ? TimeSpan.Zero : Timeout.InfiniteTimeSpan, cancellationToken)
            .ConfigureAwait(false);

        if (!mutexAcquired)
        {
            // The mutex was busy. Only the opportunistic path uses a zero timeout, so we must be opportunistic; allow a
            // retry on a different lock instance. We never acquired the mutex, so we cannot inspect the held set to
            // decide whether disposal is safe.
            Debug.Assert(opportunistic);

            return new Result(MultiplexedConnectionLockRetry.Retry, canSafelyDispose: false);
        }

        try
        {
            // Redundant with the catch below, but avoids issuing a query on a connection we already know is broken.
            if (opportunistic && IsConnectionBrokenNoLock)
            {
                return _GetAlreadyBrokenResultNoLock();
            }

            var identity = strategy.GetHeldLockIdentity(name);

            if (_heldLockIdentitiesToKeepaliveCadences.ContainsKey(identity))
            {
                // We won't hold the same physical lock twice on one connection (advisory locks are re-entrant per
                // session, so the database would grant it and both callers would wrongly believe they hold it
                // exclusively). Force the second acquirer elsewhere. See guard #1.
                return _GetFailureResultNoLock(isAlreadyHeld: true, opportunistic, timeout);
            }

            if (!_connectionOpened)
            {
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                _connectionOpened = true;
            }

            var lockCookie = await strategy
                .TryAcquireAsync(_connection, name, opportunistic ? TimeSpan.Zero : timeout, cancellationToken)
                .ConfigureAwait(false);

            if (lockCookie is not null)
            {
                // The handle is the caller's resource — ownership transfers out via the returned Result.
#pragma warning disable CA2000
                var handle = new Handle<TLockCookie>(this, strategy, name, identity, lockCookie);
#pragma warning restore CA2000
                _heldLockIdentitiesToKeepaliveCadences.Add(identity, keepaliveCadence);

                if (keepaliveCadence != Timeout.InfiniteTimeSpan)
                {
                    _SetKeepaliveCadenceNoLock();
                }

                return new Result(handle);
            }

            // We failed to acquire; retry if we were opportunistically using an artificially-shortened timeout.
            return _GetFailureResultNoLock(isAlreadyHeld: false, opportunistic, timeout);
        }
        // Never punish the caller for a connection that was already broken (https://github.com/madelson/DistributedLock/issues/83):
        // the broken connection — not the caller's request — is the failure, and the pool retries it on a fresh lock.
        // The same applies to a transient failure re-opening an idle pooled connection on the opportunistic path: the
        // open faults before _connectionOpened flips, so IsConnectionBrokenNoLock (which requires it) would miss it
        // and let the exception fault the whole acquire loop. A not-yet-opened connection holds no locks (you cannot
        // hold an advisory lock on a closed connection), so treat the failed reuse like a broken connection and retry
        // on a fresh lock rather than failing the caller.
#pragma warning disable ERP022
        catch when (opportunistic && (IsConnectionBrokenNoLock || !_connectionOpened))
        {
            return _GetAlreadyBrokenResultNoLock();
        }
#pragma warning restore ERP022
        finally
        {
            await _CloseConnectionIfNeededNoLockAsync().ConfigureAwait(false);
            _mutex.Release();
        }
    }

    /// <summary>
    /// Disposes the underlying connection. Must only be called when no locks are held on this instance
    /// (i.e. the held-lock dictionary is empty).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Debug.Assert(_heldLockIdentitiesToKeepaliveCadences.Count == 0);

        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _mutex.Dispose();
        }
    }

    /// <summary>
    /// <see langword="true"/> if the connection is currently busy (mutex held by an in-flight acquire/release) or still
    /// holds at least one lock. Used by the pool's pruning pass to decide whether the lock can be disposed.
    /// </summary>
    public async ValueTask<bool> GetIsInUseAsync()
    {
        if (!await _mutex.WaitAsync(TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false))
        {
            return true;
        }

        try
        {
            return _heldLockIdentitiesToKeepaliveCadences.Count != 0;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private Result _GetAlreadyBrokenResultNoLock()
    {
        // Retry on any already-broken connection so the death of a connection has no observable effect (other than
        // perf) compared to not multiplexing.
        return new(
            MultiplexedConnectionLockRetry.Retry,
            canSafelyDispose: _heldLockIdentitiesToKeepaliveCadences.Count == 0
        );
    }

    private Result _GetFailureResultNoLock(bool isAlreadyHeld, bool opportunistic, TimeSpan timeout)
    {
        // Only opportunistic acquisitions trigger retries.
        if (!opportunistic)
        {
            return new Result(
                MultiplexedConnectionLockRetry.NoRetry,
                canSafelyDispose: _heldLockIdentitiesToKeepaliveCadences.Count == 0
            );
        }

        if (isAlreadyHeld)
        {
            // We're already holding the physical lock, so retry on a different lock instance. We can't dispose because
            // we're holding the lock.
            return new Result(MultiplexedConnectionLockRetry.Retry, canSafelyDispose: false);
        }

        // We failed due to a timeout.
        var isHoldingLocks = _heldLockIdentitiesToKeepaliveCadences.Count != 0;

        if (timeout == TimeSpan.Zero)
        {
            // The caller asked for a zero timeout, so a timeout is a conventional failure with no retry.
            return new Result(MultiplexedConnectionLockRetry.NoRetry, canSafelyDispose: !isHoldingLocks);
        }

        if (isHoldingLocks)
        {
            // Holding other locks, so retry on another lock (don't block their release).
            return new Result(MultiplexedConnectionLockRetry.Retry, canSafelyDispose: false);
        }

        // Holding nothing: safe to retry on this instance since we can't block a release. It's also safe to dispose,
        // but we won't — we'll retry on it instead.
        return new Result(MultiplexedConnectionLockRetry.RetryOnThisLock, canSafelyDispose: true);
    }

    private async ValueTask _ReleaseAsync<TLockCookie>(
        IDbSynchronizationStrategy<TLockCookie> strategy,
        string name,
        object identity,
        TLockCookie lockCookie
    )
        where TLockCookie : class
    {
        await _mutex.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            // A broken connection has already released all of its advisory locks server-side, so the explicit unlock
            // is unnecessary and would only fault ("connection is not open"). Skipping it lets a handle whose connection
            // died (the lost-token path) dispose cleanly.
            //
            // If this unlock throws while sibling locks are still held on the shared connection, we deliberately let it
            // propagate and do NOT force-close the connection to "recover" the failed lock. Force-closing would release
            // the siblings' advisory locks server-side, but the close routes through ConnectionMonitor.StopAsync, which
            // tears down their connection-lost-token registrations *without firing them* (a stop means "clean teardown",
            // not "connection lost"). The sibling holders would then keep running their critical sections believing they
            // still hold a lock another process can now take — a silent mutual-exclusion violation. Letting the failure
            // propagate instead leaves the failed lock held server-side only until this connection closes normally (when
            // its co-tenants finish and the held-set empties): bounded extra latency on that one resource, never a safety
            // violation. This mirrors the reference engine, which only ever closes a connection once it holds nothing.
            if (_connection.CanExecuteQueries)
            {
                await strategy.ReleaseAsync(_connection, name, lockCookie).ConfigureAwait(false);
            }
        }
        finally
        {
            if (_heldLockIdentitiesToKeepaliveCadences.Remove(identity, out var keepaliveCadence))
            {
                if (keepaliveCadence != Timeout.InfiniteTimeSpan)
                {
                    // Recompute even when we're about to close, so the cadence is correct if and when we re-open.
                    _SetKeepaliveCadenceNoLock();
                }
            }

            await _CloseConnectionIfNeededNoLockAsync().ConfigureAwait(false);
            _mutex.Release();
        }
    }

    private async ValueTask _CloseConnectionIfNeededNoLockAsync()
    {
        // Close the connection (which releases every advisory lock on the session) only once the last held lock is
        // gone. We never force-close a connection that still holds sibling locks — see _ReleaseAsync for why doing so
        // would strand the siblings' connection-lost tokens and break mutual exclusion.
        if (_connectionOpened && _heldLockIdentitiesToKeepaliveCadences.Count == 0)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            _connectionOpened = false;
        }
    }

    private void _SetKeepaliveCadenceNoLock()
    {
        var minCadence = Timeout.InfiniteTimeSpan;

        foreach (var cadence in _heldLockIdentitiesToKeepaliveCadences.Values)
        {
            if (TimeSpanCadence.CompareWithInfinite(cadence, minCadence) < 0)
            {
                minCadence = cadence;
            }
        }

        _connection.SetKeepaliveCadence(minCadence);
    }

    /// <summary>
    /// Result returned by <see cref="TryAcquireAsync{TLockCookie}"/> carrying either a live handle on
    /// success or a retry recommendation plus disposal hint on failure.
    /// </summary>
    public readonly struct Result
    {
        /// <summary>Constructs a successful result carrying a live <paramref name="handle"/>.</summary>
        /// <param name="handle">The acquired <see cref="IDistributedLease"/>.</param>
        public Result(IDistributedLease handle)
        {
            Handle = handle;
            Retry = MultiplexedConnectionLockRetry.NoRetry;
            CanSafelyDispose = false; // we have a handle
        }

        /// <summary>Constructs a failure result with the given retry recommendation and disposal hint.</summary>
        /// <param name="retry">Whether the pool should retry on a different lock instance or on this one.</param>
        /// <param name="canSafelyDispose">
        /// <see langword="true"/> when this instance holds no locks and can be disposed by the pool.
        /// </param>
        public Result(MultiplexedConnectionLockRetry retry, bool canSafelyDispose)
        {
            Handle = null;
            Retry = retry;
            CanSafelyDispose = canSafelyDispose;
        }

        /// <summary>The acquired lease on success; <see langword="null"/> on failure.</summary>
        public IDistributedLease? Handle { get; }

        /// <summary>Retry recommendation for the pool when <see cref="Handle"/> is <see langword="null"/>.</summary>
        public MultiplexedConnectionLockRetry Retry { get; }

        /// <summary>
        /// <see langword="true"/> when the lock holds no advisory locks and can safely be disposed
        /// and removed from the pool.
        /// </summary>
        public bool CanSafelyDispose { get; }
    }

    private sealed class Handle<TLockCookie>(
        MultiplexedConnectionLock @lock,
        IDbSynchronizationStrategy<TLockCookie> strategy,
        string name,
        object identity,
        TLockCookie lockCookie
    ) : IDistributedLease
        where TLockCookie : class
    {
        private TLockCookie? _lockCookie = lockCookie;
        private IDatabaseConnectionMonitoringHandle? _monitoringHandle;
        private int _disposed;

        public string LeaseId { get; } = Guid.NewGuid().ToString("N");

        public long? FencingToken => null;

        public string Resource { get; } = name;

        public int RenewalCount => 0;

        public DateTimeOffset DateAcquired { get; } = @lock._connection.TimeProvider.GetUtcNow();

        public TimeSpan TimeWaitedForLock => TimeSpan.Zero;

        public bool CanObserveLoss => true;

        public CancellationToken LostToken
        {
            get
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

                // Lazily register a monitoring handle on first read. The connection outlives this handle until release,
                // so it is safe to ask its monitor for a connection-lost token here.
                //
                // MA0173 suggests LazyInitializer.EnsureInitialize here, but that is unsafe for a *disposable*:
                // the lock-free overload can run the factory on multiple racing threads and discards the losing
                // handle WITHOUT disposing it, leaking a monitoring handle on every lost race. The CAS below is
                // deliberate — it disposes the loser — so the analyzer is suppressed for this block.
#pragma warning disable MA0173
                if (Volatile.Read(ref _monitoringHandle) is null)
                {
                    var newHandle = @lock._connection.GetConnectionMonitoringHandle();

                    if (Interlocked.CompareExchange(ref _monitoringHandle, newHandle, comparand: null) is not null)
                    {
                        // Lost the race against a concurrent reader; discard ours.
                        newHandle.Dispose();
                    }
                }
#pragma warning restore MA0173

                var handle = Volatile.Read(ref _monitoringHandle);
                ObjectDisposedException.ThrowIf(handle is null, this);

                return handle.ConnectionLostToken;
            }
        }

        public async Task ReleaseAsync()
        {
            await DisposeAsync().ConfigureAwait(false);
        }

        public Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
        {
            // The advisory lock is held for the connection's lifetime; there is no lease to renew.
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(true);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _monitoringHandle, value: null)?.Dispose();

            var prevLockCookie = _lockCookie!;
            _lockCookie = null;

            await @lock._ReleaseAsync(strategy, Resource, identity, prevLockCookie).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Retry recommendation returned by <see cref="MultiplexedConnectionLock.TryAcquireAsync{TLockCookie}"/>
/// when the acquire fails on a pooled connection.
/// </summary>
internal enum MultiplexedConnectionLockRetry
{
    /// <summary>Do not retry; the caller should give up or surface a failure.</summary>
    NoRetry,

    /// <summary>Retry the acquire on this same lock instance (the connection is idle and holding nothing).</summary>
    RetryOnThisLock,

    /// <summary>Retry the acquire on a different lock instance (this one is busy, holding locks, or broken).</summary>
    Retry,
}
