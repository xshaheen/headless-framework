using Headless.DistributedLocks;
using tusdotnet.Interfaces;

namespace Headless.Tus;

public sealed class DistributedLockTusLockProvider(IDistributedLockProvider distributedLockProvider)
    : ITusFileLockProvider
{
    public Task<ITusFileLock> AquireLock(string fileId)
    {
        return Task.FromResult<ITusFileLock>(new DistributedLockTusFileLock(fileId, distributedLockProvider));
    }
}
