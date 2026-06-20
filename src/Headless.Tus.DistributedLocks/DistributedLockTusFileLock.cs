// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using tusdotnet.Interfaces;

namespace Headless.Tus;

public sealed class DistributedLockTusFileLock(string fileId, IDistributedLock distributedLockProvider) : ITusFileLock
{
    private readonly string _resource = $"tus-file-lock-{fileId}";
    private IDistributedLease? _distributedLock;

    public async Task<bool> Lock()
    {
        _distributedLock = await distributedLockProvider.TryAcquireAsync(
            _resource,
            new DistributedLockAcquireOptions
            {
                // Finite lease (provider default TTL) that auto-extends while held: a long upload keeps the lock,
                // but a crashed holder's lease expires and frees the file. An infinite lease would stay stuck
                // forever after a crash. AutoExtend requires a finite TTL, so TimeUntilExpires is left at default.
                AcquireTimeout = TimeSpan.Zero, // try once, no wait — a concurrent PATCH on the same file fails fast
                Monitoring = LockMonitoringMode.AutoExtend,
            }
        );

        return _distributedLock is not null;
    }

    public async Task ReleaseIfHeld()
    {
        if (_distributedLock is not null)
        {
            // Disposing releases the lease (ReleaseOnDispose defaults true) and stops the auto-extend monitor.
            await _distributedLock.DisposeAsync();
            _distributedLock = null;
        }
    }
}
