// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Caching;

/// <summary>
/// Per-instance keyed lock for preventing duplicate concurrent operations on the same key.
/// Used internally for cache stampede protection.
/// </summary>
internal sealed class KeyedLock
{
    private readonly Dictionary<string, RefCountedSemaphore> _semaphores = new(StringComparer.Ordinal);

    [MustDisposeResource]
    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
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

    private SemaphoreSlim _GetOrCreate(string key)
    {
        lock (_semaphores)
        {
            if (_semaphores.TryGetValue(key, out var item))
            {
                ++item.RefCount;
                return item.Semaphore;
            }

#pragma warning disable CA2000 // SemaphoreSlim disposed when RefCountedSemaphore removed from dictionary
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

    private sealed class Releaser(KeyedLock owner, string key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            lock (owner._semaphores)
            {
                if (!owner._semaphores.TryGetValue(key, out var item))
                {
                    return;
                }

                --item.RefCount;

                if (item.RefCount == 0)
                {
                    owner._semaphores.Remove(key);
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
