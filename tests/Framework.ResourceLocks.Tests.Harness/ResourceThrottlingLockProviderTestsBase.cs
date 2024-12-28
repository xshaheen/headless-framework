using System.Diagnostics;
using FluentAssertions.Extensions;
using Framework.ResourceLocks;
using Framework.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public abstract class ResourceThrottlingLockProviderTestsBase(ITestOutputHelper output) : TestBase(output)
{
    protected abstract IResourceThrottlingLockProvider GetLockProvider(int maxHits, TimeSpan period);

    public virtual async Task should_throttle_calls_async()
    {
        const int allowedLocks = 25;
        var resource = Guid.NewGuid().ToString("N")[..10];
        var period = TimeSpan.FromSeconds(2);

        var lockProvider = GetLockProvider(allowedLocks, period);

        // Sleep until start of throttling period
        await _SleepUntilStartOfPeriodAsync(period);

        // Acquire all allowed locks within the throttling period
        var timestamp = Stopwatch.GetTimestamp();

        for (var i = 1; i <= allowedLocks; i++)
        {
            Logger.LogInformation("Allowed Locks: {Id}", i);
            var l = await lockProvider.TryAcquireAsync(resource);
            l.Should().NotBeNull();
            l!.Resource.Should().Be(resource);
            l.TimeWaitedForLock.Should().BeCloseTo(TimeSpan.Zero, 200.Milliseconds());
        }

        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation("Time to acquire {AllowedLocks} locks: {Elapsed:g}", allowedLocks, elapsed);
        (elapsed.TotalSeconds < 1).Should().BeTrue();

        // Attempt to acquire a lock after all allowed locks have been acquired
        timestamp = Stopwatch.GetTimestamp();
        var result = await lockProvider.TryAcquireAsync(resource, null, new CancellationToken(true));
        elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation("Total acquire time took to attempt to get throttled lock: {Elapsed:g}", elapsed);
        result.Should().BeNull();

        // Try to acquire a lock while all allowed locks are still acquired but with
        // a acquireTimeout greater than the remaining period to reset the throttling
        timestamp = Stopwatch.GetTimestamp();
        result = await lockProvider.TryAcquireAsync(resource, acquireTimeout: TimeSpan.FromSeconds(2.5));
        elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation("Time to acquire lock: {Elapsed:g}", elapsed);
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
    }

    public virtual async Task should_throttle_concurrent_calls_async()
    {
        const int allowedLocks = 1000;
        var resource = Guid.NewGuid().ToString("N")[..10];
        var period = TimeSpan.FromSeconds(2);

        var lockProvider = GetLockProvider(allowedLocks, period);

        // Sleep until start of throttling period
        await _SleepUntilStartOfPeriodAsync(period);

        // Acquire all allowed locks within the throttling period
        var timestamp = Stopwatch.GetTimestamp();

        await Parallel.ForAsync(
            1,
            allowedLocks + 1,
            async (i, ct) =>
            {
                Logger.LogInformation("Allowed Locks: {Id}", i);
                var l = await lockProvider.TryAcquireAsync(resource, cancellationToken: ct);
                l.Should().NotBeNull();
                l!.Resource.Should().Be(resource);
                l.TimeWaitedForLock.Should().BeCloseTo(TimeSpan.Zero, 200.Milliseconds());
            }
        );

        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation("Time to acquire {AllowedLocks} locks: {Elapsed:g}", allowedLocks, elapsed);
        (elapsed.TotalSeconds < 1).Should().BeTrue();

        // Attempt to acquire a lock after all allowed locks have been acquired
        timestamp = Stopwatch.GetTimestamp();
        var result = await lockProvider.TryAcquireAsync(resource, null, new CancellationToken(true));
        elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation("Total acquire time took to attempt to get throttled lock: {Elapsed:g}", elapsed);
        result.Should().BeNull();

        // Try to acquire a lock while all allowed locks are still acquired but with
        // a acquireTimeout greater than the remaining period to reset the throttling
        timestamp = Stopwatch.GetTimestamp();
        result = await lockProvider.TryAcquireAsync(resource, acquireTimeout: TimeSpan.FromSeconds(2.5));
        elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation("Time to acquire lock: {Elapsed:g}", elapsed);
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
    }

    private static async Task _SleepUntilStartOfPeriodAsync(TimeSpan period)
    {
        while (DateTime.UtcNow.Ticks % period.Ticks < TimeSpan.TicksPerMillisecond * 100)
        {
            await Task.Delay(10);
        }
    }
}
