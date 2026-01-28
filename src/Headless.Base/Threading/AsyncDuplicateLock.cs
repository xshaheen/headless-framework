// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Threading;

/// <summary>
/// Asynchronous locking based on a string key. Useful for preventing duplicate concurrent
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
/// public async Task&lt;T&gt; GetOrCreateAsync&lt;T&gt;(string key, Func&lt;Task&lt;T&gt;&gt; factory, CancellationToken ct)
/// {
///     if (_cache.TryGetValue(key, out T cached))
///         return cached;
///
///     using (await AsyncDuplicateLock.LockAsync(key, ct))
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
public static class AsyncDuplicateLock
{
    private static readonly Dictionary<string, RefCountedSemaphore> _SemaphoreSlims = new(StringComparer.Ordinal);

    [MustDisposeResource]
    public static IDisposable Lock(string key)
    {
        Argument.IsNotNullOrEmpty(key);

        _GetOrCreate(key).Wait();

        return new Releaser(key);
    }

    [MustDisposeResource]
    public static async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);

        var semaphore = _GetOrCreate(key);

        try
        {
            await semaphore.WaitAsync(cancellationToken).AnyContext();
        }
        catch
        {
            _DecrementRefCount(key);
            throw;
        }

        return new Releaser(key);
    }

    /// <summary>
    /// Tries to acquire a lock for the specified key within the given timeout.
    /// </summary>
    /// <param name="key">The key to lock on.</param>
    /// <param name="timeout">The maximum time to wait for the lock.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// An <see cref="IDisposable"/> that releases the lock when disposed,
    /// or <c>null</c> if the lock could not be acquired within the timeout.
    /// </returns>
    [MustDisposeResource]
    public static async Task<IDisposable?> TryLockAsync(
        string key,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);

        var semaphore = _GetOrCreate(key);

        bool acquired;
        try
        {
            acquired = await semaphore.WaitAsync(timeout, cancellationToken).AnyContext();
        }
        catch
        {
            _DecrementRefCount(key);
            throw;
        }

        if (!acquired)
        {
            _DecrementRefCount(key);
            return null;
        }

        return new Releaser(key);
    }

    private static SemaphoreSlim _GetOrCreate(string key)
    {
        lock (_SemaphoreSlims)
        {
            if (_SemaphoreSlims.TryGetValue(key, out var item))
            {
                ++item.RefCount;
                return item.Semaphore;
            }

#pragma warning disable CA2000 // The SemaphoreSlim will be disposed when the RefCountedSemaphore is removed from the dictionary.
            var newItem = new RefCountedSemaphore(new SemaphoreSlim(1, 1));
#pragma warning restore CA2000
            _SemaphoreSlims[key] = newItem;
            return newItem.Semaphore;
        }
    }

    private static void _DecrementRefCount(string key)
    {
        lock (_SemaphoreSlims)
        {
            if (!_SemaphoreSlims.TryGetValue(key, out var item))
            {
                return;
            }

            --item.RefCount;

            if (item.RefCount == 0)
            {
                _SemaphoreSlims.Remove(key);
                item.Semaphore.Dispose();
            }
        }
    }

    private sealed class Releaser(string key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            lock (_SemaphoreSlims)
            {
                if (!_SemaphoreSlims.TryGetValue(key, out var item))
                {
                    return;
                }

                --item.RefCount;

                if (item.RefCount == 0)
                {
                    _SemaphoreSlims.Remove(key);
                    item.Semaphore.Release();
                    item.Semaphore.Dispose();
                }
                else
                {
                    item.Semaphore.Release();
                }
            }
        }
    }

    private sealed class RefCountedSemaphore(SemaphoreSlim semaphore)
    {
        public int RefCount { get; set; } = 1;

        public SemaphoreSlim Semaphore { get; } = semaphore;
    }
}
