using Headless.DistributedLocks;
using tusdotnet.Interfaces;

namespace Headless.Tus;

public sealed class ResourceLockTusLockProvider(IResourceLockProvider resourceLockProvider) : ITusFileLockProvider
{
    public Task<ITusFileLock> AquireLock(string fileId)
    {
        return Task.FromResult<ITusFileLock>(new ResourceLockTusFileLock(fileId, resourceLockProvider));
    }
}
