// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Public-facing handle for a connection-scoped advisory lock. The lock is released only when this handle is disposed
/// (or <see cref="ReleaseAsync"/> is called) — there is no TTL and no GC finalizer safety net: the storage holds a
/// strong reference to the underlying engine handle for its lifetime, so a consumer that abandons this handle without
/// disposing it leaks the backing connection and its advisory lock until the provider itself is disposed. This is the
/// deliberate contract for connection-scoped locks (mirroring <c>lock</c>/<c>using</c> discipline); the reference
/// engine's finalizer queue was intentionally dropped in favor of requiring explicit disposal. Always dispose the
/// handle (an <c>await using</c> is the intended usage).
/// </summary>
internal sealed class ConnectionScopedDistributedLockHandle : IDistributedLock
{
    private readonly ConnectionScopedLockHandle _handle;
    private readonly Func<ConnectionScopedLockHandle, CancellationToken, ValueTask> _release;
    private readonly bool _releaseOnDispose;
    private readonly AsyncLock _gate = new();
    private int _released;
    private int _disposed;

    public ConnectionScopedDistributedLockHandle(
        ConnectionScopedLockHandle handle,
        long? fencingToken,
        TimeSpan timeWaitedForLock,
        bool releaseOnDispose,
        TimeProvider timeProvider,
        Func<ConnectionScopedLockHandle, CancellationToken, ValueTask> release,
        ILogger logger
    )
    {
        _handle = handle;
        _release = release;
        _releaseOnDispose = releaseOnDispose;
        FencingToken = fencingToken;
        TimeWaitedForLock = timeWaitedForLock;
        DateAcquired = timeProvider.GetUtcNow();
        Logger = logger;
    }

    private ILogger Logger { get; }

    public string LockId => _handle.LockId;

    public long? FencingToken { get; }

    public string Resource => _handle.Resource;

    public int RenewalCount { get; private set; }

    public DateTimeOffset DateAcquired { get; }

    public TimeSpan TimeWaitedForLock { get; }

    public CancellationToken HandleLostToken => _handle.ConnectionLostToken;

    public bool IsMonitored => true;

    public async Task ReleaseAsync()
    {
        if (Volatile.Read(ref _released) != 0)
        {
            return;
        }

        using (await _gate.LockAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (Volatile.Read(ref _released) != 0)
            {
                return;
            }

            try
            {
                await _release(_handle, CancellationToken.None).ConfigureAwait(false);
                Volatile.Write(ref _released, 1);
            }
            catch (Exception exception)
            {
                Logger.LogConnectionScopedLockReleaseFailed(exception, Resource, LockId);
                throw;
            }
        }
    }

    public Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
    {
        // No-op for session-scoped locks: the advisory lock is held for the connection's lifetime,
        // so there is no lease to extend. RenewalCount stays at 0 to avoid signalling monitoring
        // that the lifetime was extended.
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(true);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_releaseOnDispose)
        {
            await ReleaseAsync().ConfigureAwait(false);
        }
    }
}
