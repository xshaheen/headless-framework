// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Threading;

/// <summary>
/// Instance-based asynchronous locking based on a string key. Useful for preventing duplicate concurrent
/// operations on the same resource (e.g., cache stampede protection).
/// </summary>
/// <remarks>
/// <para>
/// Each key gets its own <see cref="SemaphoreSlim"/> with a reference count. The entry leaves the key map
/// when no longer in use; each shard keeps a few idle entries for reuse and disposes the rest.
/// </para>
/// <para>
/// The dictionary is sharded into <see cref="_ShardCount"/> stripes to reduce contention under
/// high key-cardinality workloads. Each acquire/release locks only the shard that owns the key
/// rather than a single process-wide monitor.
/// </para>
/// <para>
/// <b>Locks are non-reentrant.</b> Acquisition tracks no owning thread or async context, so a caller that
/// already holds the lock for a key and re-acquires the same key without first releasing will deadlock
/// (<see cref="LockAsync(string,CancellationToken)"/>) or observe the key as held (<see cref="TryLock"/>).
/// </para>
/// <para>
/// Acquiring returns a <see cref="Releaser"/> struct, so acquiring a free key allocates nothing. The
/// <see cref="ValueTask{TResult}"/> the async overloads return must be awaited exactly once. Disposing a
/// <see cref="Releaser"/> a second time, disposing a copy after the lock was released, and disposing
/// <c>default(Releaser)</c> do nothing.
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
            _shards[i] = new Shard(this);
        }
    }

    /// <summary>
    /// Asynchronously acquires a lock for the specified key, waiting until it becomes available.
    /// </summary>
    /// <param name="key">The key to lock on.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// A <see cref="Releaser"/> that releases the lock when disposed. A free key completes synchronously. Await the
    /// returned <see cref="ValueTask{TResult}"/> exactly once. Every exception below is reported through it, not thrown
    /// by the call.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this lock has already been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before the lock is acquired.</exception>
    [MustDisposeResource]
    public ValueTask<Releaser> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_TryGetEntry(key, out var entry, out var failure))
        {
            return ValueTask.FromException<Releaser>(failure);
        }

        // Match SemaphoreSlim.WaitAsync, which reports a cancelled token even when the lock is free.
        if (cancellationToken.IsCancellationRequested)
        {
            entry.Shard.DecrementRefCount(entry);
            return ValueTask.FromCanceled<Releaser>(cancellationToken);
        }

        if (entry.Semaphore.Wait(0, CancellationToken.None))
        {
            return new(new Releaser(entry));
        }

        return _LockSlowAsync(entry, cancellationToken);
    }

    private static async ValueTask<Releaser> _LockSlowAsync(
        RefCountedSemaphore entry,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            entry.Shard.DecrementRefCount(entry);
            throw;
        }

        return new Releaser(entry);
    }

    /// <summary>
    /// Attempts to acquire the lock for the specified key without waiting. Useful for non-blocking
    /// deduplication: the first caller wins and others observe the lock as held.
    /// </summary>
    /// <param name="key">The key to lock on.</param>
    /// <returns>A <see cref="Releaser"/> that releases the lock when disposed, or <see langword="null"/> when the lock is already held.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this lock has already been disposed.</exception>
    [MustDisposeResource]
    public Releaser? TryLock(string key)
    {
        Argument.IsNotNullOrEmpty(key);
        Ensure.NotDisposed(_disposed, this);

        var entry = _GetShard(key).GetOrCreate(key);

        if (!entry.Semaphore.Wait(0))
        {
            entry.Shard.DecrementRefCount(entry);
            return null;
        }

        return new Releaser(entry);
    }

    /// <summary>
    /// Asynchronously acquires a lock for the specified key, returning <see langword="null"/> when the timeout
    /// elapses before acquisition.
    /// </summary>
    /// <param name="key">The key to lock on.</param>
    /// <param name="timeout">The acquisition timeout, or <see cref="Timeout.InfiniteTimeSpan"/> for unbounded wait.</param>
    /// <param name="timeProvider">The time provider used for deterministic timeout scheduling.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// A <see cref="Releaser"/> that releases the lock when disposed, or <see langword="null"/> on timeout. A free key
    /// completes synchronously. Await the returned <see cref="ValueTask{TResult}"/> exactly once. Every exception
    /// below is reported through it, not thrown by the call.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is not positive and is not <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this lock has already been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before the lock is acquired.</exception>
    [MustDisposeResource]
    public ValueTask<Releaser?> LockAsync(
        string key,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            Argument.IsNotNull(timeProvider);

            if (timeout != Timeout.InfiniteTimeSpan)
            {
                Argument.IsPositive(timeout);
            }
        }
        catch (ArgumentException e)
        {
            return ValueTask.FromException<Releaser?>(e);
        }

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            var unbounded = LockAsync(key, cancellationToken);

            return unbounded.IsCompletedSuccessfully ? new(unbounded.Result) : _AsNullableAsync(unbounded);
        }

        if (!_TryGetEntry(key, out var entry, out var failure))
        {
            return ValueTask.FromException<Releaser?>(failure);
        }

        // A free lock needs no timer, linked token source, or WhenAny race: take it synchronously. A caller that
        // already cancelled still gets the cancellation, as the awaited wait below would have reported it.
        if (cancellationToken.IsCancellationRequested)
        {
            entry.Shard.DecrementRefCount(entry);
            return ValueTask.FromCanceled<Releaser?>(cancellationToken);
        }

        if (entry.Semaphore.Wait(0, CancellationToken.None))
        {
            return new(new Releaser(entry));
        }

        return _LockWithTimeoutSlowAsync(entry, timeout, timeProvider, cancellationToken);
    }

    private static async ValueTask<Releaser?> _AsNullableAsync(ValueTask<Releaser> pending)
    {
        return await pending.ConfigureAwait(false);
    }

    private static async ValueTask<Releaser?> _LockWithTimeoutSlowAsync(
        RefCountedSemaphore entry,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        var shard = entry.Shard;
        var semaphore = entry.Semaphore;

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
                    shard.DecrementRefCount(entry);
                    throw;
                }

                await delayCts.CancelAsync().ConfigureAwait(false);
                return new Releaser(entry);
            }

            // Timeout elapsed: cancel the semaphore wait and clean up.
            await delayCts.CancelAsync().ConfigureAwait(false);

            try
            {
                await waitTask.ConfigureAwait(false);
                // The wait completed in the window between the race and our cancel — treat as acquired then released.
                shard.Release(entry, entry.Generation);
            }
            catch (OperationCanceledException)
            {
                // Our own delayCts triggered this; the caller did not cancel (CanBeCanceled was false).
                shard.DecrementRefCount(entry);
            }
            catch
            {
                shard.DecrementRefCount(entry);
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
                shard.DecrementRefCount(entry);
                throw;
            }

            await delayCts2.CancelAsync().ConfigureAwait(false);
            return new Releaser(entry);
        }

        await waitCts.CancelAsync().ConfigureAwait(false);

        try
        {
            await waitTask2.ConfigureAwait(false);
            shard.Release(entry, entry.Generation);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelled by waitCts (timeout path), not by the caller.
            shard.DecrementRefCount(entry);
        }
        catch
        {
            shard.DecrementRefCount(entry);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    // Validates the key and takes a reference on its entry. Failures come back as an exception instead of being
    // thrown, so the ValueTask overloads report them through the returned task, as the earlier async methods did.
    private bool _TryGetEntry(
        string key,
        [NotNullWhen(true)] out RefCountedSemaphore? entry,
        [NotNullWhen(false)] out Exception? failure
    )
    {
        try
        {
            Argument.IsNotNullOrEmpty(key);
            Ensure.NotDisposed(_disposed, this);
            entry = _GetShard(key).GetOrCreate(key);
            failure = null;
            return true;
        }
        catch (Exception e) when (e is ArgumentException or ObjectDisposedException)
        {
            entry = null;
            failure = e;
            return false;
        }
    }

    private Shard _GetShard(string key)
    {
        // Ordinal hashing (matching the per-shard dictionary comparer) so the same key always maps to the
        // same shard. string.GetHashCode(StringComparison) is a direct instance call, avoiding the virtual
        // StringComparer.Ordinal.GetHashCode dispatch on this acquire/release hot path.
        var hash = key.GetHashCode(StringComparison.Ordinal);
        return _shards[hash & (_ShardCount - 1)];
    }

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

    /// <summary>
    /// Releases the lock taken for one key when disposed. Disposing it again, disposing a copy after the lock was
    /// released, or disposing <c>default(Releaser)</c> does nothing.
    /// </summary>
    public readonly struct Releaser : IDisposable, IEquatable<Releaser>
    {
        // The entry and the generation it had when this holder acquired it. Every release advances the generation,
        // so a copy disposed after the key was released (and possibly re-acquired, or the pooled entry reused for
        // another key) sees a newer generation and does nothing. A class releaser with a disposed flag would cost an
        // allocation per acquire; the generation gives the same guarantee to every copy of the struct.
        private readonly RefCountedSemaphore? _entry;
        private readonly int _generation;

        internal Releaser(RefCountedSemaphore entry)
        {
            _entry = entry;
            _generation = entry.Generation;
        }

        /// <summary>Releases the lock if this releaser still owns it.</summary>
        public void Dispose() => _entry?.Shard.Release(_entry, _generation);

        /// <inheritdoc />
        public bool Equals(Releaser other) => ReferenceEquals(_entry, other._entry) && _generation == other._generation;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Releaser other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(_entry, _generation);

        /// <summary>Returns whether two releasers own the same acquisition.</summary>
        public static bool operator ==(Releaser left, Releaser right) => left.Equals(right);

        /// <summary>Returns whether two releasers own different acquisitions.</summary>
        public static bool operator !=(Releaser left, Releaser right) => !left.Equals(right);
    }

    // Internal rather than private only because the public Releaser's internal constructor takes it.
    internal sealed class RefCountedSemaphore
    {
        internal RefCountedSemaphore(Shard shard, SemaphoreSlim semaphore)
        {
            Shard = shard;
            Semaphore = semaphore;
        }

        // Advanced under the shard lock before the semaphore is released, and read by the next holder only after its
        // wait succeeds, so the semaphore hand-off orders the write before the read.
        public int Generation { get; set; }

        public int RefCount { get; set; } = 1;

        public string Key { get; set; } = "";

        public SemaphoreSlim Semaphore { get; }

        public Shard Shard { get; }
    }

    // Each shard owns its lock and dictionary; all bookkeeping happens inside the shard so the monitor stays a
    // private field (never exposed), keeping lock-target analysis happy and scoping contention to the shard.
    internal sealed class Shard(KeyedAsyncLock owner)
    {
        // A free key is the common case: every uncontended acquire of an unheld key would otherwise allocate a
        // SemaphoreSlim and its entry, and dispose them again on release. Idle entries are kept for reuse, but only
        // a few per shard: the pool only has to cover the keys in flight at once on one shard, and a larger cap would
        // pin memory after a burst of distinct keys that a steady workload never reclaims.
        private const int _PoolCapacity = 16;

        // Lock is .NET 9+ and performs better than lock(object) on uncontended paths.
        private readonly Lock _gate = new();

        // Idle entries, each with its semaphore free (CurrentCount 1). Allocated on first return, so a lock whose
        // shards never see traffic pays nothing for the pool. The test suite reflects on both fields.
        private RefCountedSemaphore?[]? _pool;
        private int _poolCount;

        // Set by DisposeAll under _gate. Not owner._disposed: the owner sets that before it reaches this shard, and a
        // release in that window must still hand the semaphore to a waiter, as it did when the map still held the key.
        private bool _torn;

        // Private; the test suite reflects on this field for the live count. Never used as a lock target.
        private readonly Dictionary<string, RefCountedSemaphore> _map = new(StringComparer.Ordinal);

        public RefCountedSemaphore GetOrCreate(string key)
        {
            lock (_gate)
            {
                // Re-check disposal under the shard lock: a caller can clear the outer NotDisposed gate and
                // then race DisposeAll. Without this guard a post-DisposeAll insert would leak a semaphore
                // that nothing will ever dispose. DisposeAll takes the same lock, so this read is ordered.
                Ensure.NotDisposed(owner._disposed, owner);

                if (_map.TryGetValue(key, out var item))
                {
                    ++item.RefCount;
                    return item;
                }

                RefCountedSemaphore newItem;

                if (_poolCount > 0)
                {
                    newItem = _pool![--_poolCount]!;
                    _pool[_poolCount] = null;
                    newItem.RefCount = 1;
                }
                else
                {
#pragma warning disable CA2000 // False positive: the SemaphoreSlim is disposed when its entry is evicted, pooled entries by DisposeAll
                    newItem = new RefCountedSemaphore(this, new SemaphoreSlim(1, 1));
#pragma warning restore CA2000
                }

                newItem.Key = key;
                _map[key] = newItem;
                return newItem;
            }
        }

        // Caller holds _gate and has removed the entry from the map with its ref count at 0.
        private void _Retire(RefCountedSemaphore item)
        {
            // Pool only a free semaphore: at ref count 0 no holder or waiter remains, so it is free, and the check
            // keeps a broken invariant from handing a held semaphore to the next key.
            if (_poolCount < _PoolCapacity && item.Semaphore.CurrentCount == 1)
            {
                _pool ??= new RefCountedSemaphore?[_PoolCapacity];
                _pool[_poolCount++] = item;
                return;
            }

            item.Semaphore.Dispose();
        }

        // Drops a reference taken by a caller that never acquired the semaphore (cancelled, timed out, or TryLock lost).
        public void DecrementRefCount(RefCountedSemaphore item)
        {
            lock (_gate)
            {
                // DisposeAll already disposed every entry and cleared the map.
                if (_torn)
                {
                    return;
                }

                if (--item.RefCount == 0)
                {
                    _map.Remove(item.Key);
                    _Retire(item);
                }
            }
        }

        public void Release(RefCountedSemaphore item, int generation)
        {
            lock (_gate)
            {
                // A stale or repeated dispose sees a newer generation; after DisposeAll the semaphore is gone.
                if (_torn || item.Generation != generation)
                {
                    return;
                }

                item.Generation = unchecked(generation + 1);
                item.Semaphore.Release();

                if (--item.RefCount == 0)
                {
                    _map.Remove(item.Key);
                    _Retire(item);
                }
            }
        }

        public void DisposeAll()
        {
            lock (_gate)
            {
                _torn = true;

                foreach (var item in _map.Values)
                {
                    item.Semaphore.Dispose();
                }

                _map.Clear();

                for (var i = 0; i < _poolCount; i++)
                {
                    _pool![i]!.Semaphore.Dispose();
                    _pool[i] = null;
                }

                _poolCount = 0;
            }
        }
    }
}
