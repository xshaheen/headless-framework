// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Threading;

/// <summary>
/// Instance-based asynchronous locking based on a string key. Useful for preventing duplicate concurrent
/// operations on the same resource (e.g., cache stampede protection).
/// </summary>
/// <remarks>
/// <para>
/// Each key gets its own <see cref="SemaphoreSlim"/> with a reference count. The semaphore
/// is automatically cleaned up when no longer in use.
/// </para>
/// <para>
/// The dictionary is sharded into <see cref="_ShardCount"/> stripes to reduce contention under
/// high key-cardinality workloads. Each acquire/release locks only the shard that owns the key
/// rather than a single process-wide monitor.
/// </para>
/// <para>
/// <b>Cache stampede protection example:</b>
/// </para>
/// <code>
/// var keyedLock = new KeyedAsyncLock();
///
/// public async Task&lt;T&gt; GetOrCreateAsync&lt;T&gt;(string key, Func&lt;Task&lt;T&gt;&gt; factory, CancellationToken ct)
/// {
///     if (_cache.TryGetValue(key, out T cached))
///         return cached;
///
///     using (await keyedLock.LockAsync(key, ct))
///     {
///         // Double-check after acquiring lock
///         if (_cache.TryGetValue(key, out cached))
///             return cached;
///
///         var value = await factory();
///         _cache.Set(key, value);
///         return value;
///     }
/// }
/// </code>
/// </remarks>
/// <seealso href="https://stackoverflow.com/questions/31138179/asynchronous-locking-based-on-a-key"/>
[PublicAPI]
public sealed class KeyedAsyncLock : IDisposable
{
    // Power-of-two near ProcessorCount, clamped to [8, 64].
    // Using a power of two lets shard selection reduce to a fast bitmask (hash & (_ShardCount-1))
    // instead of a division, and avoids modulo bias.
    private static readonly int _ShardCount = _ComputeShardCount();

    private static int _ComputeShardCount()
    {
        // Round ProcessorCount up to the nearest power of two, then clamp.
        var n = Environment.ProcessorCount;
        var p = 1;

        while (p < n)
        {
            p <<= 1;
        }

        return Math.Clamp(p, 8, 64);
    }

    private readonly Shard[] _shards;
    private volatile bool _disposed;

    /// <summary>Initializes a new <see cref="KeyedAsyncLock"/> instance.</summary>
    public KeyedAsyncLock()
    {
        _shards = new Shard[_ShardCount];

        for (var i = 0; i < _shards.Length; i++)
        {
            _shards[i] = new Shard();
        }
    }

    /// <summary>
    /// Asynchronously acquires a lock for the specified key, waiting until it becomes available.
    /// </summary>
    /// <param name="key">The key to lock on.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An <see cref="IDisposable"/> that releases the lock when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this lock has already been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before the lock is acquired.</exception>
    [MustDisposeResource]
    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Ensure.NotDisposed(_disposed, this);

        var semaphore = _GetOrCreate(key);

        try
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _DecrementRefCount(key);
            throw;
        }

        return new Releaser(this, key);
    }

    /// <summary>
    /// Attempts to acquire the lock for the specified key without waiting. Useful for non-blocking
    /// deduplication: the first caller wins and others observe the lock as held.
    /// </summary>
    /// <param name="key">The key to lock on.</param>
    /// <returns>An <see cref="IDisposable"/> that releases the lock when disposed, or <see langword="null"/> when the lock is already held.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this lock has already been disposed.</exception>
    [MustDisposeResource]
    public IDisposable? TryLock(string key)
    {
        Argument.IsNotNullOrEmpty(key);
        Ensure.NotDisposed(_disposed, this);

        var semaphore = _GetOrCreate(key);

        if (!semaphore.Wait(0))
        {
            _DecrementRefCount(key);
            return null;
        }

        return new Releaser(this, key);
    }

    /// <summary>
    /// Asynchronously acquires a lock for the specified key, returning <see langword="null"/> when the timeout
    /// elapses before acquisition.
    /// </summary>
    /// <param name="key">The key to lock on.</param>
    /// <param name="timeout">The acquisition timeout, or <see cref="Timeout.InfiniteTimeSpan"/> for unbounded wait.</param>
    /// <param name="timeProvider">The time provider used for deterministic timeout scheduling.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An <see cref="IDisposable"/> that releases the lock when disposed, or <see langword="null"/> on timeout.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is not positive and is not <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this lock has already been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before the lock is acquired.</exception>
    [MustDisposeResource]
    public async Task<IDisposable?> LockAsync(
        string key,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(timeProvider);
        Ensure.NotDisposed(_disposed, this);

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return await LockAsync(key, cancellationToken).ConfigureAwait(false);
        }

        Argument.IsPositive(timeout);

        var semaphore = _GetOrCreate(key);

        // When the caller token cannot be cancelled, skip the linked CTS — one allocation instead of two.
        if (!cancellationToken.CanBeCanceled)
        {
            using var delayCts = new CancellationTokenSource();
            var waitTask = semaphore.WaitAsync(delayCts.Token);
            var delayTask = Task.Delay(timeout, timeProvider, delayCts.Token);
            var winner = await Task.WhenAny(waitTask, delayTask).ConfigureAwait(false);

            if (winner == waitTask)
            {
                try
                {
                    await waitTask.ConfigureAwait(false);
                }
                catch
                {
                    await delayCts.CancelAsync().ConfigureAwait(false);
                    _DecrementRefCount(key);
                    throw;
                }

                await delayCts.CancelAsync().ConfigureAwait(false);
                return new Releaser(this, key);
            }

            // Timeout elapsed: cancel the semaphore wait and clean up.
            await delayCts.CancelAsync().ConfigureAwait(false);

            try
            {
                await waitTask.ConfigureAwait(false);
                // The wait completed in the window between the race and our cancel — treat as acquired then released.
                _Release(key);
            }
            catch (OperationCanceledException)
            {
                // Our own delayCts triggered this; the caller did not cancel (CanBeCanceled was false).
                _DecrementRefCount(key);
            }
            catch
            {
                _DecrementRefCount(key);
                throw;
            }

            return null;
        }

        // Both a finite timeout AND a cancellable caller token: link them so either can abort the wait.
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var delayCts2 = new CancellationTokenSource();
        var waitTask2 = semaphore.WaitAsync(waitCts.Token);
        var delayTask2 = Task.Delay(timeout, timeProvider, delayCts2.Token);
        var winner2 = await Task.WhenAny(waitTask2, delayTask2).ConfigureAwait(false);

        if (winner2 == waitTask2)
        {
            try
            {
                await waitTask2.ConfigureAwait(false);
            }
            catch
            {
                await delayCts2.CancelAsync().ConfigureAwait(false);
                _DecrementRefCount(key);
                throw;
            }

            await delayCts2.CancelAsync().ConfigureAwait(false);
            return new Releaser(this, key);
        }

        await waitCts.CancelAsync().ConfigureAwait(false);

        try
        {
            await waitTask2.ConfigureAwait(false);
            _Release(key);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelled by waitCts (timeout path), not by the caller.
            _DecrementRefCount(key);
        }
        catch
        {
            _DecrementRefCount(key);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private Shard _GetShard(string key)
    {
        // StringComparer.Ordinal matches the per-shard dictionary comparer, ensuring the same key
        // always maps to the same shard regardless of the calling context.
        var hash = StringComparer.Ordinal.GetHashCode(key);
        return _shards[hash & (_ShardCount - 1)];
    }

    private SemaphoreSlim _GetOrCreate(string key) => _GetShard(key).GetOrCreate(key);

    private void _DecrementRefCount(string key) => _GetShard(key).DecrementRefCount(key);

    private void _Release(string key) => _GetShard(key).Release(key);

    /// <summary>
    /// Disposes all semaphores held by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var shard in _shards)
        {
            shard.DisposeAll();
        }
    }

    private sealed class Releaser(KeyedAsyncLock owner, string key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            owner._Release(key);
        }
    }

    private sealed class RefCountedSemaphore(SemaphoreSlim semaphore)
    {
        public int RefCount { get; set; } = 1;
        public SemaphoreSlim Semaphore { get; } = semaphore;
    }

    // Each shard owns its lock and dictionary; all bookkeeping happens inside the shard so the monitor stays a
    // private field (never exposed), keeping lock-target analysis happy and scoping contention to the shard.
    private sealed class Shard
    {
        // Lock is .NET 9+ and performs better than lock(object) on uncontended paths.
        private readonly Lock _gate = new();

        // Private; the test suite reflects on this field for the live count. Never used as a lock target.
        private readonly Dictionary<string, RefCountedSemaphore> _map = new(StringComparer.Ordinal);

        public SemaphoreSlim GetOrCreate(string key)
        {
            lock (_gate)
            {
                if (_map.TryGetValue(key, out var item))
                {
                    ++item.RefCount;
                    return item.Semaphore;
                }

#pragma warning disable CA2000 // The SemaphoreSlim will be disposed when the RefCountedSemaphore is removed
                var newItem = new RefCountedSemaphore(new SemaphoreSlim(1, 1));
#pragma warning restore CA2000
                _map[key] = newItem;
                return newItem.Semaphore;
            }
        }

        public void DecrementRefCount(string key)
        {
            lock (_gate)
            {
                if (!_map.TryGetValue(key, out var item))
                {
                    return;
                }

                if (--item.RefCount == 0)
                {
                    _map.Remove(key);
                    item.Semaphore.Dispose();
                }
            }
        }

        public void Release(string key)
        {
            lock (_gate)
            {
                if (!_map.TryGetValue(key, out var item))
                {
                    return;
                }

                if (--item.RefCount == 0)
                {
                    _map.Remove(key);
                    item.Semaphore.Release();
                    item.Semaphore.Dispose();
                }
                else
                {
                    item.Semaphore.Release();
                }
            }
        }

        public void DisposeAll()
        {
            lock (_gate)
            {
                foreach (var item in _map.Values)
                {
                    item.Semaphore.Dispose();
                }

                _map.Clear();
            }
        }
    }
}
