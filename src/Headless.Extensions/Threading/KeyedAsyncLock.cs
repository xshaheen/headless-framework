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
    private readonly Dictionary<string, RefCountedSemaphore> _semaphores = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Asynchronously acquires a lock for the specified key.
    /// </summary>
    /// <param name="key">The key to lock on.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An <see cref="IDisposable"/> that releases the lock when disposed.</returns>
    [MustDisposeResource]
    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);

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
    /// Asynchronously acquires a lock for the specified key, returning <see langword="null"/> when the timeout
    /// elapses before acquisition.
    /// </summary>
    /// <param name="key">The key to lock on.</param>
    /// <param name="timeout">The acquisition timeout, or <see cref="Timeout.InfiniteTimeSpan"/> for unbounded wait.</param>
    /// <param name="timeProvider">The time provider used for deterministic timeout scheduling.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An <see cref="IDisposable"/> that releases the lock when disposed, or <see langword="null"/> on timeout.</returns>
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

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return await LockAsync(key, cancellationToken).ConfigureAwait(false);
        }

        Argument.IsPositive(timeout);

        var semaphore = _GetOrCreate(key);

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var delayCts = new CancellationTokenSource();
        var waitTask = semaphore.WaitAsync(waitCts.Token);
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

        await waitCts.CancelAsync().ConfigureAwait(false);

        try
        {
            await waitTask.ConfigureAwait(false);
            _Release(key);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
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

    private SemaphoreSlim _GetOrCreate(string key)
    {
        lock (_semaphores)
        {
            if (_semaphores.TryGetValue(key, out var item))
            {
                ++item.RefCount;
                return item.Semaphore;
            }

#pragma warning disable CA2000 // The SemaphoreSlim will be disposed when the RefCountedSemaphore is removed
            var newItem = new RefCountedSemaphore(new SemaphoreSlim(1, 1));
#pragma warning restore CA2000
            _semaphores[key] = newItem;
            return newItem.Semaphore;
        }
    }

    private void _DecrementRefCount(string key)
    {
        lock (_semaphores)
        {
            if (!_semaphores.TryGetValue(key, out var item))
            {
                return;
            }

            --item.RefCount;

            if (item.RefCount == 0)
            {
                _semaphores.Remove(key);
                item.Semaphore.Dispose();
            }
        }
    }

    private void _Release(string key)
    {
        lock (_semaphores)
        {
            if (!_semaphores.TryGetValue(key, out var item))
            {
                return;
            }

            --item.RefCount;

            if (item.RefCount == 0)
            {
                _semaphores.Remove(key);
                item.Semaphore.Release();
                item.Semaphore.Dispose();
            }
            else
            {
                item.Semaphore.Release();
            }
        }
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

        lock (_semaphores)
        {
            foreach (var item in _semaphores.Values)
            {
                item.Semaphore.Dispose();
            }

            _semaphores.Clear();
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
}
