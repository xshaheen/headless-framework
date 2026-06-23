// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>
/// TUS file lock provider that creates <c>DistributedLockTusFileLock</c> instances backed by
/// <c>IDistributedLock</c>.
/// </summary>
/// <remarks>
/// Register this as <c>ITusFileLockProvider</c> in DI alongside any
/// <c>IDistributedLock</c> implementation (Redis, SQL Server, etc.) to protect concurrent TUS
/// PATCH requests across multiple application nodes. Use
/// <c>SetupTusDistributedLock.AddDistributedLockTusLockProvider</c> for convenient registration.
/// </remarks>
[PublicAPI]
public sealed class DistributedLockTusLockProvider(IDistributedLock distributedLockProvider) : ITusFileLockProvider
{
    /// <summary>
    /// Creates a new <c>DistributedLockTusFileLock</c> for the given TUS file without acquiring
    /// the lock yet.
    /// </summary>
    /// <param name="fileId">the TUS file identifier; used as part of the distributed lock resource key</param>
    /// <returns>
    /// a new <c>ITusFileLock</c> whose <c>Lock()</c> method must be called to attempt lock
    /// acquisition
    /// </returns>
    public Task<ITusFileLock> AquireLock(string fileId)
    {
        return Task.FromResult<ITusFileLock>(new DistributedLockTusFileLock(fileId, distributedLockProvider));
    }
}
