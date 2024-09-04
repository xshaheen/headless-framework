namespace Framework.DistributedLocks;

[PublicAPI]
public interface IDistributedLockProvider
{
    Task<IDistributedLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeout = null,
        CancellationToken abortToken = default
    );
}
