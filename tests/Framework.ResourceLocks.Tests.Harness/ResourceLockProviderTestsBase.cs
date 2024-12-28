// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.ResourceLocks;
using Framework.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public abstract class ResourceLockProviderTestsBase(ITestOutputHelper output) : TestBase(output)
{
    protected abstract IResourceLockProvider GetLockProvider();

    public virtual async Task should_lock_with_try_acquire()
    {
        var lockProvider = GetLockProvider();

        await using var handle = await lockProvider.TryAcquireAsync("lock1");

        handle.Should().NotBeNull();
    }

    public virtual async Task should_not_acquire_when_already_locked()
    {
        var lockProvider = GetLockProvider();

        await using (var handle = await lockProvider.TryAcquireAsync("lock1"))
        {
            handle.Should().NotBeNull();

            await Task.Run(async () =>
            {
                await using var handle2 = await lockProvider.TryAcquireAsync("lock1");

                handle2.Should().BeNull();
            });
        }

        await Task.Run(async () =>
        {
            await using var handle = await lockProvider.TryAcquireAsync("lock1");

            handle.Should().NotBeNull();
        });
    }

    public virtual async Task should_obtain_multiple_locks()
    {
        var lockProvider = GetLockProvider();

        await using var handle = await lockProvider.TryAcquireAsync("lock1");

        handle.Should().NotBeNull();

        await Task.Run(async () =>
        {
            await using var handle2 = await lockProvider.TryAcquireAsync("lock2");

            handle2.Should().NotBeNull();
        });
    }

    public virtual async Task should_acquire_and_release_locks_async()
    {
        var locker = GetLockProvider();

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

    public virtual async Task should_release_lock_multiple_times()
    {
        var locker = GetLockProvider();

        var lock1 = await locker.TryAcquireAsync(
            "test",
            timeUntilExpires: TimeSpan.FromSeconds(1),
            acquireTimeout: TimeSpan.FromMilliseconds(100)
        );

        lock1.Should().NotBeNull();
        await lock1!.ReleaseAsync();
        (await locker.IsLockedAsync("test")).Should().BeFalse();

        var lock2 = await locker.TryAcquireAsync(
            "test",
            timeUntilExpires: TimeSpan.FromSeconds(1),
            acquireTimeout: TimeSpan.FromMilliseconds(100)
        );

        lock2.Should().NotBeNull();

        // has already been released, should not release other people's lock
        await lock1.ReleaseAsync();
        (await locker.IsLockedAsync("test")).Should().BeTrue();

        // has already been released, should not release other people's lock
        await lock1.DisposeAsync();
        (await locker.IsLockedAsync("test")).Should().BeTrue();

        await lock2!.ReleaseAsync();
        (await locker.IsLockedAsync("test")).Should().BeFalse();
    }

    public virtual async Task should_timeout_when_try_to_lock_acquired_resource()
    {
        var locker = GetLockProvider();
        const string resource = "test";

        Logger.LogInformation("Acquiring lock #1");
        var testLock = await locker.TryAcquireAsync(resource, timeUntilExpires: TimeSpan.FromMilliseconds(250));
        Logger.LogInformation(testLock != null ? "Acquired lock #1" : "Unable to acquire lock #1");
        testLock.Should().NotBeNull();

        Logger.LogInformation("Acquiring lock #2");
        testLock = await locker.TryAcquireAsync(resource, acquireTimeout: TimeSpan.FromMilliseconds(50));
        Logger.LogInformation(testLock != null ? "Acquired lock #2" : "Unable to acquire lock #2");
        testLock.Should().BeNull();

        Logger.LogInformation("Acquiring lock #3");
        testLock = await locker.TryAcquireAsync(resource, acquireTimeout: TimeSpan.FromSeconds(10));
        Logger.LogInformation(testLock != null ? "Acquired lock #3" : "Unable to acquire lock #3");
        testLock.Should().NotBeNull();
    }

    public virtual async Task should_acquire_locks_in_parallel()
    {
        var locker = GetLockProvider();

        const int count = 100;
        var current = 1;
        var used = new List<int>();
        var concurrency = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(1, count),
            async (_, ct) =>
            {
                await using var myLock = await locker.TryAcquireAsync(
                    "test",
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(1),
                    ct
                );

                myLock.Should().NotBeNull();

                var currentConcurrency = Interlocked.Increment(ref concurrency);
                currentConcurrency.Should().Be(current);

                var item = current;
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.NextInt64(5, 25)), ct);
                used.Add(item);
                current++;

                Interlocked.Decrement(ref concurrency);
            }
        );

        var duplicates = used.GroupBy(x => x).Where(g => g.Skip(1).Any());
        duplicates.Should().BeEmpty();
        used.Should().HaveCount(count);
    }

    public virtual async Task should_lock_one_at_a_time_async()
    {
        var locker = GetLockProvider();

        var successCount = 0;

        var lockTask1 = Task.Run(async () =>
        {
            if (await _DoLockedWorkAsync(locker))
            {
                Interlocked.Increment(ref successCount);
                Logger.LogInformation("LockTask1 Success");
            }
        });

        var lockTask2 = Task.Run(async () =>
        {
            if (await _DoLockedWorkAsync(locker))
            {
                Interlocked.Increment(ref successCount);
                Logger.LogInformation("LockTask2 Success");
            }
        });

        var lockTask3 = Task.Run(async () =>
        {
            if (await _DoLockedWorkAsync(locker))
            {
                Interlocked.Increment(ref successCount);
                Logger.LogInformation("LockTask3 Success");
            }
        });

        var lockTask4 = Task.Run(async () =>
        {
            if (await _DoLockedWorkAsync(locker))
            {
                Interlocked.Increment(ref successCount);
                Logger.LogInformation("LockTask4 Success");
            }
        });

        await Task.WhenAll(lockTask1, lockTask2, lockTask3, lockTask4);

        successCount.Should().Be(1);

        await Task.Run(async () =>
        {
            if (await _DoLockedWorkAsync(locker))
            {
                Interlocked.Increment(ref successCount);
            }
        });

        successCount.Should().Be(2);
    }

    private static Task<bool> _DoLockedWorkAsync(IResourceLockProvider locker)
    {
        return locker.TryUsingAsync(
            "DoLockedWork",
            async () => await Task.Delay(500),
            TimeSpan.FromMinutes(1),
            TimeSpan.Zero
        );
    }
}
