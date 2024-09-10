namespace Framework.DistributedLocks;

/// <summary>
/// A mutex synchronization primitive which can be used to coordinate access to a resource or critical region of code
/// across processes or systems. The scope and capabilities of the lock are dependent on the particular implementation
/// </summary>
[PublicAPI]
public interface IDistributedLock : IAsyncDisposable
{
    /// <summary>A unique identifier for the lock instance.</summary>
    string LockId { get; }

    /// <summary>A name that uniquely identifies the lock.</summary>
    string Resource { get; }

    /// <summary>The number of times the lock has been renewed.</summary>
    int RenewalCount { get; }

    /// <summary>The time the lock was acquired.</summary>
    DateTimeOffset DateAcquired { get; }

    /// <summary>The amount of time waited to acquire the lock.</summary>
    TimeSpan TimeWaitedForLock { get; }

    /// <summary>Releases the lock.</summary>
    Task ReleaseAsync();

    /// <summary>Attempts to renew the lock.</summary>
    Task RenewAsync(TimeSpan? timout = null);
}
