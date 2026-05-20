using System.Diagnostics;
using Headless.RateLimiting;
using Headless.Testing.Tests;
using Humanizer;
using Microsoft.Extensions.Logging;

namespace Tests;

public abstract class DistributedRateLimiterTestsBase : TestBase
{
    protected TimeProvider TimeProvider { get; } = TimeProvider.System;

    protected abstract IDistributedRateLimiterStorage GetRateLimiterStorage();

    protected IDistributedRateLimiter GetRateLimiter(int maxHits, TimeSpan period)
    {
        var options = new SlidingWindowRateLimiterOptions
        {
            RateLimitingPeriod = period,
            MaxHitsPerPeriod = maxHits,
            KeyPrefix = "test_rate_limiter:",
        };

        return new SlidingWindowDistributedRateLimiter(
            GetRateLimiterStorage(),
            options,
            TimeProvider,
            LoggerFactory.CreateLogger<SlidingWindowDistributedRateLimiter>()
        );
    }

    public virtual async Task should_rate_limit_calls_async()
    {
        const int allowedLeases = 25;
        var resource = Guid.NewGuid().ToString("N")[..10];
        var period = TimeSpan.FromSeconds(2);

        var rateLimiter = GetRateLimiter(allowedLeases, period);
        // Sleep until start of rate-limiting period
        await _SleepUntilStartOfPeriod(period, AbortToken);
        // Acquire all allowed leases within the rate-limiting period
        await _AcquireLeasesSync(rateLimiter, resource, allowedLeases);
        // Attempt to acquire a lease after all allowed leases have been acquired
        await _AssertCannotAcquireMore(rateLimiter, resource);
        // Try to acquire a lease while all allowed leases are still acquired but with
        // an acquireTimeout greater than the remaining period to reset rate limiting.
        await _AssertCanAcquireWithGreaterWait(rateLimiter, resource, TimeSpan.FromSeconds(2.5));
    }

    public virtual async Task should_rate_limit_concurrent_calls_async()
    {
        const int allowedLeases = 25;
        var resource = Guid.NewGuid().ToString("N")[..10];
        var period = TimeSpan.FromSeconds(2);

        var rateLimiter = GetRateLimiter(allowedLeases, period);
        // Sleep until start of rate-limiting period
        await _SleepUntilStartOfPeriod(period, AbortToken);
        // Acquire all allowed leases within the rate-limiting period
        await _AcquireLeasesConcurrently(rateLimiter, resource, allowedLeases);
        // Attempt to acquire a lease after all allowed leases have been acquired
        await _AssertCannotAcquireMore(rateLimiter, resource);
        // Try to acquire a lease while all allowed leases are still acquired but with
        // an acquireTimeout greater than the remaining period to reset rate limiting.
        await _AssertCanAcquireWithGreaterWait(rateLimiter, resource, TimeSpan.FromSeconds(2.5));
    }

    #region Helpers

    private static async Task _SleepUntilStartOfPeriod(TimeSpan period, CancellationToken cancellationToken)
    {
        while (DateTime.UtcNow.Ticks % period.Ticks < TimeSpan.TicksPerMillisecond * 100)
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private async Task _AcquireLeasesSync(IDistributedRateLimiter provider, string resource, int count)
    {
        var timestamp = Stopwatch.GetTimestamp();

        for (var i = 1; i <= count; i++)
        {
            Logger.LogInformation("###### Try to acquire rate-limiter leases: {Id}", i);
            var l = await provider.TryAcquireAsync(resource);
            l.Should().NotBeNull();
            l.Resource.Should().Be(resource);
            l.TimeWaitedForLease.Should().BeCloseTo(TimeSpan.Zero, 300.Milliseconds());
        }

        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation(
            "###### Time to acquire {AllowedLeases} rate-limiter leases: {Elapsed:g}",
            count,
            elapsed
        );
        elapsed.TotalSeconds.Should().BeLessThan(2);
    }

    private async Task _AcquireLeasesConcurrently(IDistributedRateLimiter provider, string resource, int count)
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
        Logger.LogInformation(
            "###### Time to acquire {AllowedLeases} rate-limiter leases: {Elapsed:g}",
            count,
            elapsed
        );
        elapsed.TotalSeconds.Should().BeLessThan(5);
    }

    private async Task _AssertCannotAcquireMore(IDistributedRateLimiter provider, string resource)
    {
        var timestamp = Stopwatch.GetTimestamp();
        var result = await provider.TryAcquireAsync(resource, 50.Milliseconds(), AbortToken);
        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation(
            "###### Total acquire time took to attempt to get rate-limiter lease: {Elapsed:g}",
            elapsed
        );
        result.Should().BeNull();
    }

    private async Task _AssertCanAcquireWithGreaterWait(
        IDistributedRateLimiter provider,
        string resource,
        TimeSpan acquireTimeout
    )
    {
        var timestamp = Stopwatch.GetTimestamp();
        var result = await provider.TryAcquireAsync(resource, acquireTimeout: acquireTimeout);
        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        Logger.LogInformation("Time to acquire rate-limiter lease: {Elapsed:g}", elapsed);
        result.Should().NotBeNull();
        result.Resource.Should().Be(resource);
    }

    #endregion
}
