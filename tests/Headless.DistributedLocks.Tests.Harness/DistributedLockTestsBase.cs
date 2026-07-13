// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Humanizer;
using Microsoft.Extensions.Logging;

namespace Tests;

public abstract class DistributedLockTestsBase : TestBase
{
    protected static readonly IGuidGenerator GuidGenerator = new SequentialGuidGenerator(SequentialGuidType.Version7);
    protected static readonly TimeProvider TimeProvider = TimeProvider.System;
    protected static readonly DistributedLockOptions Options = new() { KeyPrefix = "test:" };

    protected abstract IDistributedLock GetLockProvider();

    /// <summary>
    /// Kills the backing session/connection that holds <paramref name="handle"/> from a separate admin connection,
    /// simulating a silent connection death (no clean close, no client-side RST). Connection-scoped providers
    /// (SQL Server <c>KILL</c>, PostgreSQL <c>pg_terminate_backend</c>) override this so the cross-provider
    /// connection-death scenario runs against them; lease-based or in-process providers leave it unsupported.
    /// </summary>
    protected virtual Task KillLockHoldingConnectionAsync(IDistributedLease handle, CancellationToken cancellationToken)
    {
        throw new NotSupportedException(
            "This provider does not back its lock with a killable connection; "
                + $"override {nameof(KillLockHoldingConnectionAsync)} to run the connection-death scenario."
        );
    }

    public virtual async Task should_lock_with_try_acquire()
    {
        var lockProvider = GetLockProvider();
        var resource = Faker.Random.String2(3, 20);
        await using var handle = await lockProvider.TryAcquireAsync(resource);

        handle.Should().NotBeNull();
        handle.Resource.Should().Be(resource);
        handle.LeaseId.Should().NotBeNullOrEmpty();
        handle.DateAcquired.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
        handle.RenewalCount.Should().Be(0);
        handle.TimeWaitedForLock.Should().BePositive();
    }

    public virtual async Task should_lock_with_acquire()
    {
        var lockProvider = GetLockProvider();
        var resource = Faker.Random.String2(3, 20);
        await using var handle = await lockProvider.AcquireAsync(resource);

        handle.Resource.Should().Be(resource);
        handle.LeaseId.Should().NotBeNullOrEmpty();
        handle.DateAcquired.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
        handle.RenewalCount.Should().Be(0);
        handle.TimeWaitedForLock.Should().BePositive();
    }

    public virtual async Task should_not_acquire_when_already_locked()
    {
        var lockProvider = GetLockProvider();
        var resource = Faker.Random.String2(3, 20);
        var acquireTimeout = TimeSpan.FromSeconds(1);
        var options = new DistributedLockAcquireOptions { AcquireTimeout = acquireTimeout };

        await using (var handle = await lockProvider.TryAcquireAsync(resource, options))
        {
            handle.Should().NotBeNull();

            await Task.Run(async () =>
            {
                await using var handle2 = await lockProvider.TryAcquireAsync(resource, options);

                handle2.Should().BeNull();
            });
        }

        await Task.Run(async () =>
        {
            await using var handle = await lockProvider.TryAcquireAsync(resource, options);

            handle.Should().NotBeNull();
        });
    }

    public virtual async Task should_throw_timeout_with_acquire_when_already_locked()
    {
        var lockProvider = GetLockProvider();
        var resource = Faker.Random.String2(3, 20);

        await using var handle = await lockProvider.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(1) }
        );

        var act = async () =>
            await lockProvider.AcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero }
            );

        var assertion = await act.Should().ThrowAsync<LockAcquisitionTimeoutException>();
        assertion.Which.Resource.Should().Be(resource);
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

    public virtual async Task should_acquire_composite_in_canonical_order_and_deduplicate()
    {
        var lockProvider = GetLockProvider();
        var (first, second) = _CreateCompositeResources();
        var handle = await lockProvider.AcquireAllAsync([second, first, second], cancellationToken: AbortToken);

        try
        {
            handle.Resource.Should().Be($"{first}+{second}");
            (await lockProvider.IsLockedAsync(first, AbortToken)).Should().BeTrue();
            (await lockProvider.IsLockedAsync(second, AbortToken)).Should().BeTrue();
        }
        finally
        {
            await handle.DisposeAsync();
        }

        (await lockProvider.IsLockedAsync(first, AbortToken)).Should().BeFalse();
        (await lockProvider.IsLockedAsync(second, AbortToken)).Should().BeFalse();
    }

    public virtual async Task should_acquire_opposite_composite_orders_sequentially()
    {
        var lockProvider = GetLockProvider();
        var (first, second) = _CreateCompositeResources();
        var options = new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(5) };
        var firstHandle = await lockProvider.AcquireAllAsync([second, first], options, AbortToken);
        var secondTask = lockProvider.TryAcquireAllAsync([first, second], options, AbortToken);
        var contentionWindow = Task.Delay(TimeSpan.FromMilliseconds(150), TimeProvider, AbortToken);
        var completedDuringContention = await Task.WhenAny(secondTask, contentionWindow);

        await firstHandle.ReleaseAsync();
        await firstHandle.DisposeAsync();

        var secondHandle = await secondTask.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider, AbortToken);
        completedDuringContention.Should().BeSameAs(contentionWindow);
        secondHandle.Should().NotBeNull();
        await secondHandle!.DisposeAsync();
    }

    public virtual async Task should_release_earlier_composite_children_when_later_resource_is_contended()
    {
        var lockProvider = GetLockProvider();
        var (first, second) = _CreateCompositeResources();
        await using var blocker = await lockProvider.AcquireAsync(second, cancellationToken: AbortToken);

        var handle = await lockProvider.TryAcquireAllAsync(
            [second, first, first],
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        handle.Should().BeNull();
        (await lockProvider.IsLockedAsync(first, AbortToken)).Should().BeFalse();
        (await lockProvider.IsLockedAsync(second, AbortToken)).Should().BeTrue();
    }

    public virtual async Task should_renew_and_release_composite_lease()
    {
        var lockProvider = GetLockProvider();
        var (first, second) = _CreateCompositeResources();
        var handle = await lockProvider.AcquireAllAsync([first, second], cancellationToken: AbortToken);

        try
        {
            (await handle.RenewAsync(TimeSpan.FromSeconds(30), AbortToken)).Should().BeTrue();
            await handle.ReleaseAsync();

            (await lockProvider.IsLockedAsync(first, AbortToken)).Should().BeFalse();
            (await lockProvider.IsLockedAsync(second, AbortToken)).Should().BeFalse();
        }
        finally
        {
            await handle.ReleaseAsync();
            await handle.DisposeAsync();
        }
    }

    public virtual async Task should_dispatch_composite_renew_and_release_through_provider()
    {
        var lockProvider = GetLockProvider();
        var (first, second) = _CreateCompositeResources();
        var handle = await lockProvider.AcquireAllAsync(
            [first, second],
            new DistributedLockAcquireOptions { ReleaseOnDispose = false },
            AbortToken
        );

        try
        {
            (await lockProvider.RenewAsync(handle, TimeSpan.FromSeconds(30), AbortToken)).Should().BeTrue();
            await lockProvider.ReleaseAsync(handle, AbortToken);

            (await lockProvider.IsLockedAsync(first, AbortToken)).Should().BeFalse();
            (await lockProvider.IsLockedAsync(second, AbortToken)).Should().BeFalse();
        }
        finally
        {
            await lockProvider.ReleaseAsync(handle, AbortToken);
            await handle.DisposeAsync();
        }
    }

    public virtual async Task should_keep_composite_resources_when_disposed_without_release()
    {
        var lockProvider = GetLockProvider();
        var (first, second) = _CreateCompositeResources();
        var handle = await lockProvider.AcquireAllAsync(
            [first, second],
            new DistributedLockAcquireOptions { ReleaseOnDispose = false },
            AbortToken
        );

        await handle.DisposeAsync();

        (await lockProvider.IsLockedAsync(first, AbortToken)).Should().BeTrue();
        (await lockProvider.IsLockedAsync(second, AbortToken)).Should().BeTrue();

        await handle.ReleaseAsync();

        (await lockProvider.IsLockedAsync(first, AbortToken)).Should().BeFalse();
        (await lockProvider.IsLockedAsync(second, AbortToken)).Should().BeFalse();
    }

    private static (string First, string Second) _CreateCompositeResources()
    {
        var prefix = $"composite:{Guid.NewGuid():N}";

        return ($"{prefix}:a", $"{prefix}:b");
    }

    public virtual async Task should_release_lock_multiple_times()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);

        var lock1 = await locker.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(1),
                AcquireTimeout = TimeSpan.FromMilliseconds(100),
            }
        );

        lock1.Should().NotBeNull();
        await lock1.ReleaseAsync();
        (await locker.IsLockedAsync(resource)).Should().BeFalse();

        var lock2 = await locker.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(1),
                AcquireTimeout = TimeSpan.FromMilliseconds(100),
            }
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

    public virtual async Task should_keep_lock_when_disposed_with_release_on_dispose_false()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);

        var handle = await locker.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(30),
                AcquireTimeout = TimeSpan.FromMilliseconds(100),
                ReleaseOnDispose = false,
            }
        );

        handle.Should().NotBeNull();

        await handle.DisposeAsync();

        (await locker.IsLockedAsync(resource)).Should().BeTrue();
        await locker.ReleaseAsync(resource, handle.LeaseId);
    }

    public virtual async Task should_release_explicitly_when_release_on_dispose_false()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);

        await using var handle = await locker.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(30),
                AcquireTimeout = TimeSpan.FromMilliseconds(100),
                ReleaseOnDispose = false,
            }
        );

        await handle.ReleaseAsync();
        await handle.DisposeAsync();

        (await locker.IsLockedAsync(resource)).Should().BeFalse();
    }

    public virtual async Task should_timeout_when_try_to_lock_acquired_resource()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);

        Logger.LogInformation("################## Acquiring lock #1");
        var testLock = await locker.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromMilliseconds(250) }
        );
        Logger.LogInformation(testLock != null ? "Acquired lock #1" : "Unable to acquire lock #1");
        testLock.Should().NotBeNull();

        Logger.LogInformation("################## Acquiring lock #2");
        testLock = await locker.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMilliseconds(50) }
        );
        Logger.LogInformation(testLock != null ? "Acquired lock #2" : "Unable to acquire lock #2");
        testLock.Should().BeNull();

        Logger.LogInformation("################## Acquiring lock #3");
        testLock = await locker.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(10) }
        );
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
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(1),
                AcquireTimeout = TimeSpan.FromMilliseconds(200),
            }
        );

        try
        {
            // then is locked
            lock1.Should().NotBeNull();
            (await locker.IsLockedAsync(resource)).Should().BeTrue();

            // Cannot acquire a lock on the same resource
            var lock2Task = locker.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMilliseconds(250) }
            );
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
                new DistributedLockAcquireOptions
                {
                    TimeUntilExpires = TimeSpan.FromMinutes(2),
                    AcquireTimeout = TimeSpan.FromMinutes(2),
                }
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
                    new DistributedLockAcquireOptions
                    {
                        TimeUntilExpires = TimeSpan.FromMinutes(2),
                        AcquireTimeout = TimeSpan.FromMinutes(2),
                    },
                    ct
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

        static Task<bool> doLockedWorkAsync(IDistributedLock locker, string resource)
        {
            return locker.TryUsingAsync(
                resource: resource,
                work: async () => await Task.Delay(300),
                new DistributedLockAcquireOptions
                {
                    TimeUntilExpires = TimeSpan.FromMinutes(1),
                    AcquireTimeout = TimeSpan.Zero, // No waiting just single try
                }
            );
        }
    }

    #region Observability Tests

    public virtual async Task should_get_expiration_for_locked_resource()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);
        var ttl = TimeSpan.FromMinutes(5);

        await using var handle = await locker.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { TimeUntilExpires = ttl }
        );
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

        await using var handle = await locker.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { TimeUntilExpires = ttl }
        );
        handle.Should().NotBeNull();

        var info = await locker.GetLockInfoAsync(resource);

        info.Should().NotBeNull();
        info.Resource.Should().Be(resource);
        info.LeaseId.Should().Be(handle.LeaseId);
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

    #region Lease Monitoring Tests

    public virtual async Task should_expose_none_handle_lost_token_without_monitoring()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);

        await using var handle = await locker.TryAcquireAsync(resource);

        handle.Should().NotBeNull();
        handle!.LostToken.Should().Be(CancellationToken.None);
    }

    public virtual async Task should_keep_lock_alive_when_auto_extend_is_enabled_smoke()
    {
        // Provider-level smoke test using real timers with a generous budget so wall-clock jitter
        // does not flake CI. Deterministic auto-extend / handle-lost coverage lives in
        // LeaseLifecycleIntegrationTests under FakeTimeProvider.
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 10);

        await using var handle = await locker.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(2),
                Monitoring = LockMonitoringMode.AutoExtend,
            }
        );

        handle.Should().NotBeNull();
        await Task.Delay(TimeSpan.FromSeconds(3), AbortToken);

        (await locker.IsLockedAsync(resource, AbortToken)).Should().BeTrue();
        handle!.LostToken.IsCancellationRequested.Should().BeFalse();
    }

    /// <summary>
    /// Connection-scoped providers back the lock with a dedicated session. When that session dies silently
    /// (network drop with no RST, or an out-of-band <c>KILL</c>), the provider's active liveness probe must
    /// surface the loss by cancelling the handle's <see cref="IDistributedLease.LostToken"/>. The probe
    /// runs on a bounded cadence, so the wait here is generous on purpose. Only wired by providers that
    /// override <see cref="KillLockHoldingConnectionAsync"/>.
    /// </summary>
    public virtual async Task should_fire_handle_lost_token_when_lock_holding_connection_dies()
    {
        var locker = GetLockProvider();
        var resource = Faker.Random.String2(3, 20);

        await using var handle = await locker.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions { Monitoring = LockMonitoringMode.Monitor },
            cancellationToken: AbortToken
        );

        handle.LostToken.CanBeCanceled.Should().BeTrue();
        handle.LostToken.IsCancellationRequested.Should().BeFalse();

        // Kill the lock-holding session out-of-band; the provider's probe should observe the dead connection.
        await KillLockHoldingConnectionAsync(handle, AbortToken);

        // Probe cadence is ~30s; poll generously (independent of AbortToken so we observe the lost token itself).
        var lostToken = handle.LostToken;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(40);

        while (!lostToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), AbortToken);
        }

        lostToken
            .IsCancellationRequested.Should()
            .BeTrue("the lock-holding connection died and the probe should detect it");
    }

    #endregion
}
