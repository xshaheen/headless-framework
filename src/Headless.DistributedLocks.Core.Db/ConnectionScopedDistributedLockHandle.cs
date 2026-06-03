// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

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
        cancellationToken.ThrowIfCancellationRequested();
        RenewalCount++;

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
