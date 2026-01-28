using System.Diagnostics;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Humanizer;
using Microsoft.Extensions.Logging;

namespace Tests;

public abstract class ResourceThrottlingLockProviderTestsBase : TestBase
{
    protected TimeProvider TimeProvider { get; } = TimeProvider.System;

    protected abstract IThrottlingResourceLockStorage GetLockStorage();

    protected IThrottlingResourceLockProvider GetLockProvider(int maxHits, TimeSpan period)
    {
        var options = new ThrottlingResourceLockOptions
        {
            ThrottlingPeriod = period,
            MaxHitsPerPeriod = maxHits,
            KeyPrefix = "test_throttling_lock:",
        };

        return new ThrottlingResourceLockProvider(
            GetLockStorage(),
            options,
            TimeProvider,
            LoggerFactory.CreateLogger<ThrottlingResourceLockProvider>()
        );
    }

    public virtual async Task should_throttle_calls_async()
    {
        const int allowedLocks = 25;
        var resource = Guid.NewGuid().ToString("N")[..10];
        var period = TimeSpan.FromSeconds(2);

        var lockProvider = GetLockProvider(allowedLocks, period);
        // Sleep until start of throttling period
        await _SleepUntilStartOfPeriod(period);
        // Acquire all allowed locks within the throttling period
        await _AcquireLocksSync(lockProvider, resource, allowedLocks);
        // Attempt to acquire a lock after all allowed locks have been acquired
        await _AssertCannotAcquireMore(lockProvider, resource);
        // Try to acquire a lock while all allowed locks are still acquired but with
        // a acquireTimeout greater than the remaining period to reset the throttling
        await _AssertCanAcquireWithGreaterWait(lockProvider, resource, TimeSpan.FromSeconds(2.5));
    }

    public virtual async Task should_throttle_concurrent_calls_async()
    {
        const int allowedLocks = 25;
        var resource = Guid.NewGuid().ToString("N")[..10];
        var period = TimeSpan.FromSeconds(2);

        var lockProvider = GetLockProvider(allowedLocks, period);
        // Sleep until start of throttling period
        await _SleepUntilStartOfPeriod(period);
        // Acquire all allowed locks within the throttling period
        await _AcquireLocksConcurrently(lockProvider, resource, allowedLocks);
        // Attempt to acquire a lock after all allowed locks have been acquired
        await _AssertCannotAcquireMore(lockProvider, resource);
        // Try to acquire a lock while all allowed locks are still acquired but with
        // a acquireTimeout greater than the remaining period to reset the throttling
        await _AssertCanAcquireWithGreaterWait(lockProvider, resource, TimeSpan.FromSeconds(2.5));
    }

    #region Helpers

    private static async Task _SleepUntilStartOfPeriod(TimeSpan period)
    {
        while (DateTime.UtcNow.Ticks % period.Ticks < TimeSpan.TicksPerMillisecond * 100)
        {
            await Task.Delay(10);
        }
    }

    private async Task _AcquireLocksSync(IThrottlingResourceLockProvider provider, string resource, int count)
    {
        var timestamp = Stopwatch.GetTimestamp();

        for (var i = 1; i <= count; i++)
        {
            Logger.LogInformation("###### Try to Acquire Locks: {Id}", i);
            var l = await provider.TryAcquireAsync(resource);
            l.Should().NotBeNull();
            l.Resource.Should().Be(resource);
            l.TimeWaitedForLock.Should().BeCloseTo(TimeSpan.Zero, 300.Milliseconds());
        }

        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation("###### Time to acquire {AllowedLocks} locks: {Elapsed:g}", count, elapsed);
        elapsed.TotalSeconds.Should().BeLessThan(2);
    }

    private async Task _AcquireLocksConcurrently(IThrottlingResourceLockProvider provider, string resource, int count)
    {
        var timestamp = Stopwatch.GetTimestamp();

        await Parallel.ForAsync(
            1,
            count + 1,
            async (_, ct) =>
            {
                var l = await provider.TryAcquireAsync(resource, cancellationToken: ct);
                l.Should().NotBeNull();
                l.Resource.Should().Be(resource);
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation("###### Time to acquire {AllowedLocks} locks: {Elapsed:g}", count, elapsed);
        elapsed.TotalSeconds.Should().BeLessThan(5);
    }

    private async Task _AssertCannotAcquireMore(IThrottlingResourceLockProvider provider, string resource)
    {
        var timestamp = Stopwatch.GetTimestamp();
        var result = await provider.TryAcquireAsync(resource, null, new CancellationToken(true));
        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation("###### Total acquire time took to attempt to get throttled lock: {Elapsed:g}", elapsed);
        result.Should().BeNull();
    }

    private async Task _AssertCanAcquireWithGreaterWait(
        IThrottlingResourceLockProvider provider,
        string resource,
        TimeSpan acquireTimeout
    )
    {
        var timestamp = Stopwatch.GetTimestamp();
        var result = await provider.TryAcquireAsync(resource, acquireTimeout: acquireTimeout);
        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation("Time to acquire lock: {Elapsed:g}", elapsed);
        result.Should().NotBeNull();
        result.Resource.Should().Be(resource);
    }

    #endregion
}
