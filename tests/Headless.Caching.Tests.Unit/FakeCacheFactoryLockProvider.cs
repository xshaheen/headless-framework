// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Caching;

namespace Tests;

/// <summary>
/// In-process <see cref="ICacheFactoryLockProvider"/> backed by per-key semaphores, instrumented with
/// acquire/release counts. <see cref="Hold"/> simulates another node owning the lock (its handle bypasses
/// the instrumentation so the counts only reflect the coordinator under test).
/// </summary>
internal sealed class FakeCacheFactoryLockProvider : ICacheFactoryLockProvider
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    // Plain fields (not auto-properties): Interlocked.Increment requires a ref to the storage location.
#pragma warning disable IDE0032 // Use auto property
    private int _acquireAttempts;
    private int _acquireSuccesses;
    private int _releases;
#pragma warning restore IDE0032

    public int AcquireAttempts => Volatile.Read(ref _acquireAttempts);

    public int AcquireSuccesses => Volatile.Read(ref _acquireSuccesses);

    public int Releases => Volatile.Read(ref _releases);

    /// <summary>When set, <see cref="TryAcquireAsync"/> throws (simulates a down lock backend, not "held elsewhere").</summary>
    public Func<Exception>? AcquireFault { get; set; }

    /// <summary>When set, the lease's <see cref="IAsyncDisposable.DisposeAsync"/> throws after freeing the semaphore.</summary>
    public Func<Exception>? ReleaseFault { get; set; }

    public bool IsHeld(string key)
    {
        return _locks.TryGetValue(key, out var semaphore) && semaphore.CurrentCount == 0;
    }

    /// <summary>Acquires the key's lock out-of-band, simulating a remote node holding it.</summary>
    public IDisposable Hold(string key)
    {
        var semaphore = _GetSemaphore(key);

        if (!semaphore.Wait(TimeSpan.Zero))
        {
            throw new InvalidOperationException($"The lock for key '{key}' is already held.");
        }

        return new ExternalHold(semaphore);
    }

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(
        string key,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        Interlocked.Increment(ref _acquireAttempts);

        if (AcquireFault is { } acquireFault)
        {
            throw acquireFault();
        }

        var semaphore = _GetSemaphore(key);

        var acquired =
            timeout == Timeout.InfiniteTimeSpan
                ? await _WaitUnboundedAsync(semaphore, cancellationToken)
                : await semaphore.WaitAsync(timeout, cancellationToken);

        if (!acquired)
        {
            return null;
        }

        Interlocked.Increment(ref _acquireSuccesses);

        return new Releaser(this, semaphore);
    }

    private static async Task<bool> _WaitUnboundedAsync(SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        return true;
    }

    private SemaphoreSlim _GetSemaphore(string key)
    {
        return _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
    }

    private sealed class Releaser(FakeCacheFactoryLockProvider owner, SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                // Free the semaphore before faulting so a release failure never deadlocks later acquisitions
                // in the same test; the fault only models the provider surfacing an error to the coordinator.
                semaphore.Release();

                if (owner.ReleaseFault is { } releaseFault)
                {
                    throw releaseFault();
                }

                Interlocked.Increment(ref owner._releases);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ExternalHold(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
