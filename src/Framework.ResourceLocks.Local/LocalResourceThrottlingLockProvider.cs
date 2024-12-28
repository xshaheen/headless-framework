// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Jitbit.Utils;

namespace Framework.ResourceLocks.Local;

public sealed class LocalResourceThrottlingLockStorage : IThrottlingResourceLockStorage
{
    private readonly FastCache<string, ResourceLock> _resources = new();

    public ValueTask<long> GetHitCountsAsync(string resource, long defaultValue = 0)
    {
        var results = _resources.TryGet(resource, out var resourceLock) ? resourceLock.HitsCount : defaultValue;

        return ValueTask.FromResult(results);
    }

    public ValueTask<long> IncrementAsync(string resource, TimeSpan ttl)
    {
        var resourceLock = _resources.GetOrAdd(resource, new ResourceLock(), ttl);

        resourceLock.Hit();

        return ValueTask.FromResult(resourceLock.HitsCount);
    }

    public ValueTask DisposeAsync()
    {
        _resources.Dispose();

        return ValueTask.CompletedTask;
    }

    private sealed record ResourceLock
    {
        public long HitsCount { get; private set; }

        public void Hit() => HitsCount++;
    }
}
