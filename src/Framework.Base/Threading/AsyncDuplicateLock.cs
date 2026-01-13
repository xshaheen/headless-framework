// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Threading;

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
/// public async Task&lt;T&gt; GetOrCreateAsync&lt;T&gt;(string key, Func&lt;Task&lt;T&gt;&gt; factory)
/// {
///     if (_cache.TryGetValue(key, out T cached))
///         return cached;
///
///     using (await AsyncDuplicateLock.LockAsync(key))
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
    private static readonly Dictionary<string, RefCounted<SemaphoreSlim>> _SemaphoreSlims = new(StringComparer.Ordinal);

    [MustDisposeResource]
    public static IDisposable Lock(string key)
    {
        _GetOrCreate(key).Wait();

        return new Releaser(key);
    }

    public static async Task<IDisposable> LockAsync(string key)
    {
        await _GetOrCreate(key).WaitAsync().AnyContext();

        return new Releaser(key);
    }

    private static SemaphoreSlim _GetOrCreate(string key)
    {
        RefCounted<SemaphoreSlim>? item;

        lock (_SemaphoreSlims)
        {
            if (_SemaphoreSlims.TryGetValue(key, out item))
            {
                ++item.RefCount;
            }
            else
            {
#pragma warning disable CA2000 // The SemaphoreSlim will be disposed when the RefCounted is removed from the dictionary.
                item = new RefCounted<SemaphoreSlim>(new SemaphoreSlim(1, 1));
#pragma warning restore CA2000
                _SemaphoreSlims[key] = item;
            }
        }

        return item.Value;
    }

    private sealed class Releaser(string key) : IDisposable
    {
        public void Dispose()
        {
            lock (_SemaphoreSlims)
            {
                var item = _SemaphoreSlims[key];
                --item.RefCount;

                if (item.RefCount == 0)
                {
                    _SemaphoreSlims.Remove(key);
                    item.Value.Release();
                    item.Value.Dispose();
                }
                else
                {
                    item.Value.Release();
                }
            }
        }
    }

    private sealed class RefCounted<T>(T value)
    {
        public int RefCount { get; set; } = 1;

        public T Value { get; private set; } = value;
    }
}
