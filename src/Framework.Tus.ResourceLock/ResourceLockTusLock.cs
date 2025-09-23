// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.ResourceLocks;
using tusdotnet.Interfaces;

namespace Framework.Tus;

public sealed class ResourceLockTusFileLock(string fileId, IResourceLockProvider resourceLockProvider) : ITusFileLock
{
    private readonly string _resource = $"tus-file-lock-{fileId}";
    private IResourceLock? _resourceLock;

    public async Task<bool> Lock()
    {
        _resourceLock = await resourceLockProvider.TryAcquireAsync(
            _resource,
            timeUntilExpires: Timeout.InfiniteTimeSpan, // Lock never expires
            acquireTimeout: TimeSpan.Zero // Do not wait to acquire the lock
        );

        return _resourceLock is not null;
    }

    public Task ReleaseIfHeld()
    {
        return _resourceLock is not null
            ? resourceLockProvider.ReleaseAsync(_resourceLock.Resource, _resourceLock.LockId)
            : Task.CompletedTask;
    }
}
