// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.ResourceLocks;

namespace Tests;

public abstract class ResourceLockProviderTestsBase
{
    protected abstract IResourceLockProvider? GetLockProvider();

    public virtual async Task CanAcquireAndReleaseLockAsync()
    {
        var locker = GetLockProvider();

        if (locker is null)
        {
            return;
        }

        // Try to acquire a lock
        await using var lock1 = await locker.TryAcquireAsync(
            resource: "test",
            timeUntilExpires: TimeSpan.FromSeconds(1),
            acquireTimeout: TimeSpan.FromMilliseconds(100)
        );

        lock1.Should().NotBeNull();
        lock1!.LockId.Should().NotBeNullOrEmpty();
        lock1.DateAcquired.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
        lock1.Resource.Should().Be("test");
        lock1.RenewalCount.Should().Be(0);
        lock1.TimeWaitedForLock.Should().BePositive();
        (await locker.IsLockedAsync("test")).Should().BeTrue();

        // Try to acquire a lock on the same resource after it expires
        var lock2Task = locker.TryAcquireAsync(resource: "test", acquireTimeout: TimeSpan.FromMilliseconds(250));
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        (await lock2Task).Should().BeNull();
        (await locker.IsLockedAsync("test")).Should().BeFalse();

        var counter = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(1, 25),
            async (_, token) =>
            {
                var success = await locker.TryUsingAsync(
                    resource: "test",
                    work: () =>
                    {
                        Interlocked.Increment(ref counter);
                    },
                    acquireTimeout: TimeSpan.FromSeconds(10),
                    cancellationToken: token
                );

                success.Should().BeTrue();
            }
        );

        counter.Should().Be(25);
    }
}
