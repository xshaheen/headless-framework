// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using tusdotnet.Interfaces;

namespace Headless.Tus;

public sealed class DistributedLockTusFileLock(string fileId, IDistributedLockProvider distributedLockProvider)
    : ITusFileLock
{
    private readonly string _resource = $"tus-file-lock-{fileId}";
    private IDistributedLock? _distributedLock;

    public async Task<bool> Lock()
    {
        _distributedLock = await distributedLockProvider.TryAcquireAsync(
            _resource,
            timeUntilExpires: Timeout.InfiniteTimeSpan, // Lock never expires
            acquireTimeout: TimeSpan.Zero // Do not wait to acquire the lock
        );

        return _distributedLock is not null;
    }

    public Task ReleaseIfHeld()
    {
        return _distributedLock is not null
            ? distributedLockProvider.ReleaseAsync(_distributedLock.Resource, _distributedLock.LockId)
            : Task.CompletedTask;
    }
}
