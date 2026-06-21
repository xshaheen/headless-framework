// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>
/// TUS file lock backed by <c>IDistributedLock</c>, enabling cross-process and cross-node PATCH
/// safety for TUS uploads.
/// </summary>
/// <remarks>
/// Each instance manages the lock lifecycle for one TUS file identified by
/// <paramref name="fileId"/>. The underlying lock resource key is prefixed with
/// <c>tus-file-lock-</c> to avoid collisions with other application-level distributed locks.
/// The lock is acquired with a zero-wait timeout (fail fast, no blocking), and with an infinite
/// expiry so it persists until released explicitly.
/// </remarks>
public sealed class DistributedLockTusFileLock(string fileId, IDistributedLock distributedLockProvider) : ITusFileLock
{
    private readonly string _resource = $"tus-file-lock-{fileId}";
    private IDistributedLease? _distributedLock;

    /// <summary>
    /// Attempts to acquire the distributed lock for this TUS file without waiting.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the lock was acquired; <see langword="false"/> if another
    /// process or node already holds it
    /// </returns>
    public async Task<bool> Lock()
    {
        _distributedLock = await distributedLockProvider
            .TryAcquireAsync(
                _resource,
                new DistributedLockAcquireOptions
                {
                    TimeUntilExpires = Timeout.InfiniteTimeSpan, // Lock never expires
                    AcquireTimeout = TimeSpan.Zero, // Do not wait to acquire the lock
                }
            )
            .ConfigureAwait(false);

        return _distributedLock is not null;
    }

    /// <summary>
    /// Releases the distributed lock if this instance holds it; otherwise does nothing.
    /// </summary>
    /// <returns>a completed task when no lock is held; otherwise the release task</returns>
    public Task ReleaseIfHeld()
    {
        return _distributedLock is not null
            ? distributedLockProvider.ReleaseAsync(_distributedLock.Resource, _distributedLock.LeaseId)
            : Task.CompletedTask;
    }
}
