namespace Framework.DistributedLocks;

/// <summary>
/// A mutex synchronization primitive which can be used to coordinate access to a resource or critical region of code
/// across processes or systems. The scope and capabilities of the lock are dependent on the particular implementation
/// </summary>
public interface IDistributedLock : IAsyncDisposable
{
    /// <summary>A name that uniquely identifies the lock.</summary>
    string Resource { get; }
}
