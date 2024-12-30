// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Core;

namespace Framework.ResourceLocks.Local;

public sealed class LocalResourceThrottlingLockStorage : IThrottlingResourceLockStorage
{
    private readonly CacheDictionary<string, ResourceLock> _resources = new();

    public ValueTask<long> GetHitCountsAsync(string resources, long defaultValue = 0)
    {
        var results = _resources.TryGet(resources, out var resourceLock) ? resourceLock.HitsCount : defaultValue;

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
        private long _hitsCount;

        public long HitsCount => Interlocked.Read(ref _hitsCount);

        public void Hit() => Interlocked.Increment(ref _hitsCount);
    }
}
