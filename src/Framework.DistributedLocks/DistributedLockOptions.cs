namespace Framework.DistributedLocks;

public sealed class DistributedLockOptions
{
    /// <summary>DistributedLock key prefix.</summary>
    public string KeyPrefix { get; set; } = "";
}
