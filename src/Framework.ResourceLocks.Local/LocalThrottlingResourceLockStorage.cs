// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Core;

namespace Framework.ResourceLocks.Local;

public sealed class LocalThrottlingResourceLockStorage : IThrottlingResourceLockStorage
{
    private readonly CacheDictionary<string, ResourceLock> _resources = new();

    public Task<long> GetHitCountsAsync(string resource)
    {
        var results = _resources.TryGet(resource, out var resourceLock) ? resourceLock.HitsCount : 0;

        return Task.FromResult(results);
    }

    public Task<long> IncrementAsync(string resource, TimeSpan ttl)
    {
        var resourceLock = _resources.GetOrAdd(resource, new ResourceLock(), ttl);

        resourceLock.Hit();

        return Task.FromResult(resourceLock.HitsCount);
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
