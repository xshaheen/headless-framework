// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Humanizer;
using Microsoft.Extensions.Logging;

namespace Tests;

public abstract class ResourceLockProviderTestsBase : TestBase
{
    protected static readonly SnowflakeIdLongIdGenerator LongGenerator = new(1);
    protected static readonly SequentialAsStringGuidGenerator GuidGenerator = new();
    protected static readonly TimeProvider TimeProvider = TimeProvider.System;
    protected static readonly ResourceLockOptions Options = new() { KeyPrefix = "test:" };

    protected abstract IResourceLockProvider GetLockProvider();

    public virtual async Task should_lock_with_try_acquire()
    {
        var lockProvider = GetLockProvider();
        var resource = Faker.Random.String2(3, 20);
        await using var handle = await lockProvider.TryAcquireAsync(resource);

        handle.Should().NotBeNull();
        handle.Resource.Should().Be(resource);
        handle.LockId.Should().NotBeNullOrEmpty();
        handle.DateAcquired.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
        handle.RenewalCount.Should().Be(0);
        handle.TimeWaitedForLock.Should().BePositive();
    }

    public virtual async Task should_not_acquire_when_already_locked()
    {
        var lockProvider = GetLockProvider();
        var resource = Faker.Random.String2(3, 20);
        var acquireTimeout = TimeSpan.FromSeconds(1);

        await using (var handle = await lockProvider.TryAcquireAsync(resource, null, acquireTimeout))
        {
            handle.Should().NotBeNull();

            await Task.Run(async () =>
            {
                await using var handle2 = await lockProvider.TryAcquireAsync(resource, null, acquireTimeout);

                handle2.Should().BeNull();
            });
        }

        await Task.Run(async () =>
        {
            await using var handle = await lockProvider.TryAcquireAsync(resource, null, acquireTimeout);

            handle.Should().NotBeNull();
        });
    }

    public virtual async Task should_obtain_multiple_locks()
    {
        var lockProvider = GetLockProvider();
        var resource1 = Faker.Random.String2(3, 20);
        var resource2 = Faker.Random.String2(3, 20);

        await using var handle = await lockProvider.TryAcquireAsync(resource1);

        handle.Should().NotBeNull();

        await Task.Run(async () =>
        {
            await using var handle2 = await lockProvider.TryAcquireAsync(resource2);

            handle2.Should().NotBeNull();
        });
    }

    public virtual async Task should_release_lock_multiple_times()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);

        var lock1 = await locker.TryAcquireAsync(
            resource,
            timeUntilExpires: TimeSpan.FromSeconds(1),
            acquireTimeout: TimeSpan.FromMilliseconds(100)
        );

        lock1.Should().NotBeNull();
        await lock1.ReleaseAsync();
        (await locker.IsLockedAsync(resource)).Should().BeFalse();

        var lock2 = await locker.TryAcquireAsync(
            resource,
            timeUntilExpires: TimeSpan.FromSeconds(1),
            acquireTimeout: TimeSpan.FromMilliseconds(100)
        );

        lock2.Should().NotBeNull();

        // has already been released, should not release other people's lock
        await lock1.ReleaseAsync();
        (await locker.IsLockedAsync(resource)).Should().BeTrue();

        // has already been released, should not release other people's lock
        await lock1.DisposeAsync();
        (await locker.IsLockedAsync(resource)).Should().BeTrue();

        await lock2.ReleaseAsync();
        (await locker.IsLockedAsync(resource)).Should().BeFalse();
    }

    public virtual async Task should_timeout_when_try_to_lock_acquired_resource()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);

        Logger.LogInformation("################## Acquiring lock #1");
        var testLock = await locker.TryAcquireAsync(resource, timeUntilExpires: TimeSpan.FromMilliseconds(250));
        Logger.LogInformation(testLock != null ? "Acquired lock #1" : "Unable to acquire lock #1");
        testLock.Should().NotBeNull();

        Logger.LogInformation("################## Acquiring lock #2");
        testLock = await locker.TryAcquireAsync(resource, acquireTimeout: TimeSpan.FromMilliseconds(50));
        Logger.LogInformation(testLock != null ? "Acquired lock #2" : "Unable to acquire lock #2");
        testLock.Should().BeNull();

        Logger.LogInformation("################## Acquiring lock #3");
        testLock = await locker.TryAcquireAsync(resource, acquireTimeout: TimeSpan.FromSeconds(10));
        Logger.LogInformation(testLock != null ? "Acquired lock #3" : "Unable to acquire lock #3");
        testLock.Should().NotBeNull();
    }

    public virtual async Task should_acquire_and_release_locks_async()
    {
        var locker = GetLockProvider();
        var resource = Guid.NewGuid().ToString("N")[..5];

        // Try to acquire a lock
        var lock1 = await locker.TryAcquireAsync(
            resource,
            timeUntilExpires: TimeSpan.FromSeconds(1),
            acquireTimeout: TimeSpan.FromMilliseconds(200)
        );

        try
        {
            // then is locked
            lock1.Should().NotBeNull();
            (await locker.IsLockedAsync(resource)).Should().BeTrue();

            // Cannot acquire a lock on the same resource
            var lock2Task = locker.TryAcquireAsync(resource, acquireTimeout: TimeSpan.FromMilliseconds(250));
            await Task.Delay(TimeSpan.FromMilliseconds(250), TimeProvider);
            (await lock2Task).Should().BeNull();
        }
        finally
        {
            if (lock1 is not null)
            {
                await lock1.ReleaseAsync();
            }
        }

        (await locker.IsLockedAsync(resource)).Should().BeFalse();
    }

    public virtual async Task should_acquire_one_at_a_time_parallel()
    {
        var locker = GetLockProvider();
        var resource = Guid.NewGuid().ToString("N")[..5];
        const int count = 500;

        var counter = 0;

        // Acquire 25 locks in parallel, but it should be one at a time
        await Parallel.ForEachAsync(
            Enumerable.Range(1, count),
            async (_, ct) =>
            {
                var success = await locker.TryUsingAsync(
                    resource,
                    work: () => Interlocked.Increment(ref counter),
                    acquireTimeout: 10.Seconds(),
                    cancellationToken: ct
                );

                success.Should().BeTrue();
            }
        );

        counter.Should().Be(count);
    }

    public virtual async Task should_acquire_locks_in_sync()
    {
        var locker = GetLockProvider();

        const int count = 100;
        var current = 1;
        var used = new List<int>();
        var concurrency = 0;

        for (var i = 0; i < count; i++)
        {
            await using var myLock = await locker.TryAcquireAsync(
                resource: "test",
                timeUntilExpires: TimeSpan.FromMinutes(2),
                acquireTimeout: TimeSpan.FromMinutes(2)
            );

            myLock.Should().NotBeNull();

            ++concurrency;
            concurrency.Should().Be(1);

            var item = current;
            await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.NextInt64(5, 25)), TimeProvider);
            used.Add(item);
            current++;

            --concurrency;
        }

        var duplicates = used.GroupBy(x => x).Where(g => g.Skip(1).Any());
        duplicates.Should().BeEmpty();
        used.Should().HaveCount(count);
    }

    public virtual async Task should_acquire_locks_in_parallel()
    {
        var locker = GetLockProvider();

        const int count = 150;
        var current = 1;
        var used = new List<int>();
        var concurrency = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(1, count),
            async (_, ct) =>
            {
                await using var myLock = await locker.TryAcquireAsync(
                    resource: "test",
                    timeUntilExpires: TimeSpan.FromMinutes(2),
                    acquireTimeout: TimeSpan.FromMinutes(2),
                    cancellationToken: ct
                );

                myLock.Should().NotBeNull();

                var currentConcurrency = Interlocked.Increment(ref concurrency);
                currentConcurrency.Should().Be(1);

                var item = current;
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.NextInt64(5, 25)), TimeProvider, ct);
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
        var resource = Guid.NewGuid().ToString("N")[..5];

        var lockTask1 = Task.Run(async () =>
        {
            if (await doLockedWorkAsync(locker, resource))
            {
                Interlocked.Increment(ref successCount);
                Logger.LogInformation("LockTask1 Success");
            }
        });

        var lockTask2 = Task.Run(async () =>
        {
            if (await doLockedWorkAsync(locker, resource))
            {
                Interlocked.Increment(ref successCount);
                Logger.LogInformation("LockTask2 Success");
            }
        });

        var lockTask3 = Task.Run(async () =>
        {
            if (await doLockedWorkAsync(locker, resource))
            {
                Interlocked.Increment(ref successCount);
                Logger.LogInformation("LockTask3 Success");
            }
        });

        var lockTask4 = Task.Run(async () =>
        {
            if (await doLockedWorkAsync(locker, resource))
            {
                Interlocked.Increment(ref successCount);
                Logger.LogInformation("LockTask4 Success");
            }
        });

        await Task.WhenAll(lockTask1, lockTask2, lockTask3, lockTask4);

        successCount.Should().Be(1);

        await Task.Run(async () =>
        {
            if (await doLockedWorkAsync(locker, resource))
            {
                Interlocked.Increment(ref successCount);
            }
        });

        successCount.Should().Be(2);

        static Task<bool> doLockedWorkAsync(IResourceLockProvider locker, string resource)
        {
            return locker.TryUsingAsync(
                resource: resource,
                work: async () => await Task.Delay(300),
                timeUntilExpires: TimeSpan.FromMinutes(1),
                acquireTimeout: TimeSpan.Zero // No waiting just single try
            );
        }
    }

    #region Observability Tests

    public virtual async Task should_get_expiration_for_locked_resource()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);
        var ttl = TimeSpan.FromMinutes(5);

        await using var handle = await locker.TryAcquireAsync(resource, timeUntilExpires: ttl);
        handle.Should().NotBeNull();

        var expiration = await locker.GetExpirationAsync(resource);

        expiration.Should().NotBeNull();
        expiration!.Value.Should().BeCloseTo(ttl, TimeSpan.FromSeconds(5));
    }

    public virtual async Task should_return_null_expiration_when_not_locked()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);

        var expiration = await locker.GetExpirationAsync(resource);

        expiration.Should().BeNull();
    }

    public virtual async Task should_get_lock_info_for_locked_resource()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);
        var ttl = TimeSpan.FromMinutes(5);

        await using var handle = await locker.TryAcquireAsync(resource, timeUntilExpires: ttl);
        handle.Should().NotBeNull();

        var info = await locker.GetLockInfoAsync(resource);

        info.Should().NotBeNull();
        info.Resource.Should().Be(resource);
        info.LockId.Should().Be(handle.LockId);
        info.TimeToLive.Should().NotBeNull();
        info.TimeToLive!.Value.Should().BeCloseTo(ttl, TimeSpan.FromSeconds(5));
    }

    public virtual async Task should_return_null_lock_info_when_not_locked()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);

        var info = await locker.GetLockInfoAsync(resource);

        info.Should().BeNull();
    }

    public virtual async Task should_list_active_locks()
    {
        var locker = GetLockProvider();
        var resource1 = $"list-test-{Faker.Random.String2(3, 10)}";
        var resource2 = $"list-test-{Faker.Random.String2(3, 10)}";

        await using var handle1 = await locker.TryAcquireAsync(resource1);
        await using var handle2 = await locker.TryAcquireAsync(resource2);

        handle1.Should().NotBeNull();
        handle2.Should().NotBeNull();

        var locks = await locker.ListActiveLocksAsync();

        locks.Should().Contain(l => l.Resource == resource1);
        locks.Should().Contain(l => l.Resource == resource2);
    }

    public virtual async Task should_get_active_locks_count()
    {
        var locker = GetLockProvider();

        var initialCount = await locker.GetActiveLocksCountAsync();

        var resource = $"count-test-{Faker.Random.String2(3, 10)}";
        await using var handle = await locker.TryAcquireAsync(resource);
        handle.Should().NotBeNull();

        var countAfterAcquire = await locker.GetActiveLocksCountAsync();
        countAfterAcquire.Should().BeGreaterThan(initialCount);
    }

    #endregion
}
