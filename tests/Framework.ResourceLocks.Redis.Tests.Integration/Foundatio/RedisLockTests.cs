using System.Diagnostics;
using Foundatio.Caching;
using Foundatio.Lock;
using Foundatio.Messaging;
using Framework.Caching;
using Framework.Messaging;
using Framework.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests.Foundatio;

public sealed class RedisLockTests : LockTestBase
{
    private readonly RedisCacheClient _cache;
    private readonly RedisMessageBus _messageBus;

    public RedisLockTests(ITestOutputHelper output)
        : base(output)
    {
        var muxer = SharedConnection.GetMuxer(LoggerFactory);
        muxer.FlushAllAsync().RunSynchronously();
        _cache = new RedisCacheClient(o => o.ConnectionMultiplexer(muxer).LoggerFactory(LoggerFactory).Serializer(FoundationHelper.JsonSerializer));

        _messageBus = new RedisMessageBus(o =>
            o.Subscriber(muxer.GetSubscriber()).Topic("test-lock").LoggerFactory(LoggerFactory)
        );
    }

    protected override ILockProvider GetLockProvider()
    {
        return new CacheLockProvider(_cache, _messageBus, null, LoggerFactory);
    }

    [Fact]
    public override Task CanAcquireAndReleaseLockAsync()
    {
        return base.CanAcquireAndReleaseLockAsync();
    }

    [Fact]
    public override Task LockWillTimeoutAsync()
    {
        return base.LockWillTimeoutAsync();
    }

    [Fact]
    public override Task LockOneAtATimeAsync()
    {
        return base.LockOneAtATimeAsync();
    }

    [Fact]
    public override Task CanAcquireMultipleResources()
    {
        return base.CanAcquireMultipleResources();
    }

    [Fact]
    public override Task CanAcquireLocksInParallel()
    {
        return base.CanAcquireLocksInParallel();
    }

    [Fact]
    public override Task CanAcquireScopedLocksInParallel()
    {
        return base.CanAcquireScopedLocksInParallel();
    }

    [Fact]
    public override Task CanAcquireMultipleLocksInParallel()
    {
        return base.CanAcquireMultipleLocksInParallel();
    }

    [Fact]
    public override Task CanAcquireMultipleScopedResources()
    {
        return base.CanAcquireMultipleScopedResources();
    }

    [Fact]
    public override Task CanReleaseLockMultipleTimes()
    {
        return base.CanReleaseLockMultipleTimes();
    }

    [Fact]
    public async Task LockWontTimeoutEarly()
    {
        var locker = GetLockProvider();

        if (locker == null)
        {
            return;
        }

        Logger.LogInformation("Acquiring lock #1");
        var testLock = await locker.AcquireAsync("test", timeUntilExpires: TimeSpan.FromSeconds(1));
        Logger.LogInformation(testLock != null ? "Acquired lock #1" : "Unable to acquire lock #1");
        testLock.Should().NotBeNull();

        Logger.LogInformation("Acquiring lock #2");
        var testLock2 = await locker.AcquireAsync("test", acquireTimeout: TimeSpan.FromMilliseconds(500));
        testLock2.Should().BeNull();

        Logger.LogInformation("Renew lock #1");
        await testLock!.RenewAsync(timeUntilExpires: TimeSpan.FromSeconds(1));

        Logger.LogInformation("Acquiring lock #3");
        testLock = await locker.AcquireAsync("test", acquireTimeout: TimeSpan.FromMilliseconds(500));
        testLock.Should().BeNull();

        var sw = Stopwatch.StartNew();
        Logger.LogInformation("Acquiring lock #4");
        testLock = await locker.AcquireAsync("test", acquireTimeout: TimeSpan.FromSeconds(5));
        sw.Stop();
        Logger.LogInformation(testLock != null ? "Acquired lock #3" : "Unable to acquire lock #4");
        testLock.Should().NotBeNull();
        (sw.ElapsedMilliseconds > 400).Should().BeTrue();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _cache.Dispose();
            _messageBus.Dispose();
            var muxer = SharedConnection.GetMuxer(LoggerFactory);
            muxer.FlushAllAsync().RunSynchronously();
        }
    }
}

public abstract class LockTestBase(ITestOutputHelper output) : TestBase(output)
{
    protected virtual ILockProvider? GetLockProvider()
    {
        return null;
    }

    public virtual async Task CanAcquireAndReleaseLockAsync()
    {
        var locker = GetLockProvider();

        if (locker == null)
        {
            return;
        }

        var lock1 = await locker.AcquireAsync(
            "test",
            acquireTimeout: TimeSpan.FromMilliseconds(100),
            timeUntilExpires: TimeSpan.FromSeconds(1)
        );

        try
        {
            lock1.Should().NotBeNull();
            (await locker.IsLockedAsync("test")).Should().BeTrue();
            var lock2Task = locker.AcquireAsync("test", acquireTimeout: TimeSpan.FromMilliseconds(250));
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            (await lock2Task).Should().BeNull();
        }
        finally
        {
            await lock1.ReleaseAsync();
        }

        (await locker.IsLockedAsync("test")).Should().BeFalse();

        var counter = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(1, 25),
            async (_, _) =>
            {
                var success = await locker.TryUsingAsync(
                    "test",
                    () =>
                    {
                        Interlocked.Increment(ref counter);
                    },
                    acquireTimeout: TimeSpan.FromSeconds(10)
                );

                success.Should().BeTrue();
            }
        );

        counter.Should().Be(25);
    }

    public virtual async Task CanReleaseLockMultipleTimes()
    {
        var locker = GetLockProvider();

        if (locker == null)
        {
            return;
        }

        var lock1 = await locker.AcquireAsync(
            "test",
            timeUntilExpires: TimeSpan.FromSeconds(1),
            acquireTimeout: TimeSpan.FromMilliseconds(100)
        );

        await lock1.ReleaseAsync();
        (await locker.IsLockedAsync("test")).Should().BeFalse();

        var lock2 = await locker.AcquireAsync(
            "test",
            acquireTimeout: TimeSpan.FromMilliseconds(100),
            timeUntilExpires: TimeSpan.FromSeconds(1)
        );

        // has already been released, should not release other people's lock
        await lock1.ReleaseAsync();
        (await locker.IsLockedAsync("test")).Should().BeTrue();

        // has already been released, should not release other people's lock
        await lock1.DisposeAsync();
        (await locker.IsLockedAsync("test")).Should().BeTrue();

        await lock2.ReleaseAsync();
        (await locker.IsLockedAsync("test")).Should().BeFalse();
    }

    public virtual async Task LockWillTimeoutAsync()
    {
        var locker = GetLockProvider();

        if (locker == null)
        {
            return;
        }

        Logger.LogInformation("Acquiring lock #1");
        var testLock = await locker.AcquireAsync("test", timeUntilExpires: TimeSpan.FromMilliseconds(250));
        Logger.LogInformation(testLock != null ? "Acquired lock #1" : "Unable to acquire lock #1");
        testLock.Should().NotBeNull();

        Logger.LogInformation("Acquiring lock #2");
        testLock = await locker.AcquireAsync("test", acquireTimeout: TimeSpan.FromMilliseconds(50));
        Logger.LogInformation(testLock != null ? "Acquired lock #2" : "Unable to acquire lock #2");
        testLock.Should().BeNull();

        Logger.LogInformation("Acquiring lock #3");
        testLock = await locker.AcquireAsync("test", acquireTimeout: TimeSpan.FromSeconds(10));
        Logger.LogInformation(testLock != null ? "Acquired lock #3" : "Unable to acquire lock #3");
        testLock.Should().NotBeNull();
    }

    public virtual async Task CanAcquireMultipleResources()
    {
        var locker = GetLockProvider();

        if (locker == null)
        {
            return;
        }

        var resources = new List<string> { "test1", "test2", "test3", "test4", "test5" };
        var testLock = await locker.AcquireAsync(resources, timeUntilExpires: TimeSpan.FromMilliseconds(250));
        Logger.LogInformation(testLock != null ? "Acquired lock #1" : "Unable to acquire lock #1");
        testLock.Should().NotBeNull();

        resources.Add("other");

        var testLock2 = await locker.AcquireAsync(
            resources,
            timeUntilExpires: TimeSpan.FromMilliseconds(250),
            acquireTimeout: TimeSpan.FromMilliseconds(10)
        );

        Logger.LogInformation(testLock2 != null ? "Acquired lock #1" : "Unable to acquire lock #1");
        testLock2.Should().BeNull();

        await testLock!.RenewAsync();
        await testLock.ReleaseAsync();

        var testLock3 = await locker.AcquireAsync(
            resources,
            timeUntilExpires: TimeSpan.FromMilliseconds(250),
            acquireTimeout: TimeSpan.FromMilliseconds(10)
        );

        Logger.LogInformation(testLock3 != null ? "Acquired lock #1" : "Unable to acquire lock #1");
        testLock3.Should().NotBeNull();
    }

    public virtual async Task CanAcquireMultipleScopedResources()
    {
        var locker = GetLockProvider();

        if (locker == null)
        {
            return;
        }

        locker = new ScopedLockProvider(locker, "myscope");

        var resources = new List<string> { "test1", "test2", "test3", "test4", "test5" };
        var testLock = await locker.AcquireAsync(resources, timeUntilExpires: TimeSpan.FromMilliseconds(250));
        Logger.LogInformation(testLock != null ? "Acquired lock #1" : "Unable to acquire lock #1");
        testLock.Should().NotBeNull();

        resources.Add("other");

        var testLock2 = await locker.AcquireAsync(
            resources,
            timeUntilExpires: TimeSpan.FromMilliseconds(250),
            acquireTimeout: TimeSpan.FromMilliseconds(10)
        );

        Logger.LogInformation(testLock2 != null ? "Acquired lock #1" : "Unable to acquire lock #1");
        testLock2.Should().BeNull();

        await testLock!.RenewAsync();
        await testLock.ReleaseAsync();

        var testLock3 = await locker.AcquireAsync(
            resources,
            timeUntilExpires: TimeSpan.FromMilliseconds(250),
            acquireTimeout: TimeSpan.FromMilliseconds(10)
        );

        Logger.LogInformation(testLock3 != null ? "Acquired lock #1" : "Unable to acquire lock #1");
        testLock3.Should().NotBeNull();
    }

    public virtual async Task CanAcquireLocksInParallel()
    {
        var locker = GetLockProvider();

        if (locker == null)
        {
            return;
        }

        const int count = 100;
        var current = 1;
        var used = new List<int>();
        var concurrency = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(1, count),
            async (_, ct) =>
            {
                await using var myLock = await locker.AcquireAsync(
                    "test",
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(1)
                );

                myLock.Should().NotBeNull();

                var currentConcurrency = Interlocked.Increment(ref concurrency);
                currentConcurrency.Should().Be(1);

                var item = current;
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.NextInt64(5, 25)), ct);
                used.Add(item);
                current++;

                Interlocked.Decrement(ref concurrency);
            }
        );

        var duplicates = used.GroupBy(x => x).Where(g => g.Count() > 1);
        duplicates.Should().BeEmpty();
        used.Should().HaveCount(count);
    }

    public virtual async Task CanAcquireScopedLocksInParallel()
    {
        var lockProvider = GetLockProvider();

        if (lockProvider == null)
        {
            return;
        }

        var locker = new ScopedLockProvider(lockProvider, "scoped");

        const int count = 100;
        var current = 1;
        var used = new List<int>();
        var concurrency = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(1, count),
            async (_, ct) =>
            {
                await using var myLock = await locker.AcquireAsync(
                    "test",
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(1)
                );

                myLock.Should().NotBeNull();

                var currentConcurrency = Interlocked.Increment(ref concurrency);
                currentConcurrency.Should().Be(1);

                var item = current;
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.NextInt64(5, 25)), ct);
                used.Add(item);
                current++;

                Interlocked.Decrement(ref concurrency);
            }
        );

        var duplicates = used.GroupBy(x => x).Where(g => g.Count() > 1);
        duplicates.Should().BeEmpty();
        used.Should().HaveCount(count);
    }

    public virtual async Task CanAcquireMultipleLocksInParallel()
    {
        var locker = GetLockProvider();

        if (locker == null)
        {
            return;
        }

        const int count = 100;
        var current = 1;
        var used = new List<int>();
        var concurrency = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(1, count),
            async (_, ct) =>
            {
                await using var myLock = await locker.AcquireAsync(
                    ["test", "test2"],
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(1)
                );

                myLock.Should().NotBeNull();

                var currentConcurrency = Interlocked.Increment(ref concurrency);
                currentConcurrency.Should().Be(1);

                var item = current;
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.NextInt64(5, 25)), ct);
                used.Add(item);
                current++;

                Interlocked.Decrement(ref concurrency);
            }
        );

        var duplicates = used.GroupBy(x => x).Where(g => g.Count() > 1);
        duplicates.Should().BeEmpty();
        used.Should().HaveCount(count);
    }

    public virtual async Task LockOneAtATimeAsync()
    {
        var locker = GetLockProvider();

        if (locker == null)
        {
            return;
        }

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

    private static Task<bool> _DoLockedWorkAsync(ILockProvider locker)
    {
        return locker.TryUsingAsync(
            "DoLockedWork",
            async () => await Task.Delay(500),
            TimeSpan.FromMinutes(1),
            TimeSpan.Zero
        );
    }
}
