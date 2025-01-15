namespace Tests.Lock;

public interface ILock : IAsyncDisposable
{
    string LockId { get; }

    string Resource { get; }

    DateTimeOffset DateAcquired { get; }

    TimeSpan TimeWaitedForLock { get; }

    int RenewalCount { get; }

    Task ReleaseAsync();

    Task RenewAsync(TimeSpan? timeUntilExpires = null);
}
