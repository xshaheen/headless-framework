// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Threading;

/// <summary>
/// Asynchronous locking based on a string key.
/// <a href="https://stackoverflow.com/questions/31138179/asynchronous-locking-based-on-a-key">See this stackoverflow question</a>
/// </summary>
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
#pragma warning disable CA2000
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
                }

                item.Value.Release();
            }
        }
    }

    private sealed class RefCounted<T>(T value)
    {
        public int RefCount { get; set; } = 1;

        public T Value { get; private set; } = value;
    }
}
