// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Caching;

/// <summary>
/// Instance-based asynchronous locking based on a string key. Used internally by cache implementations
/// for stampede protection.
/// </summary>
/// <remarks>
/// Each key gets its own <see cref="SemaphoreSlim"/> with a reference count. The semaphore
/// is automatically cleaned up when no longer in use.
/// </remarks>
[PublicAPI]
public sealed class KeyedAsyncLock
{
    private readonly Dictionary<string, RefCountedSemaphore> _semaphores = new(StringComparer.Ordinal);

    [MustDisposeResource]
    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken = default)
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
