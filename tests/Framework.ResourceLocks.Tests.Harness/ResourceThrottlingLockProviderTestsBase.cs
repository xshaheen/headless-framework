using System.Diagnostics;
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

        var period = TimeSpan.FromSeconds(2);
        var locker = GetLockProvider(allowedLocks, period);

        var lockName = Guid.NewGuid().ToString("N")[..10];

        // sleep until start of throttling period
        while (DateTime.UtcNow.Ticks % period.Ticks < TimeSpan.TicksPerMillisecond * 100)
        {
            await Task.Delay(10);
        }

        var sw = Stopwatch.StartNew();

        for (var i = 1; i <= allowedLocks; i++)
        {
            Logger.LogInformation("Allowed Locks: {Id}", i);
            var l = await locker.TryAcquireAsync(lockName);
            l.Should().NotBeNull();
        }
        sw.Stop();

        Logger.LogInformation("Time to acquire {AllowedLocks} locks: {Elapsed:g}", allowedLocks, sw.Elapsed);
        (sw.Elapsed.TotalSeconds < 1).Should().BeTrue();

        sw.Restart();
        var result = await locker.TryAcquireAsync(lockName, null, new CancellationToken(true));
        sw.Stop();
        Logger.LogInformation("Total acquire time took to attempt to get throttled lock: {Elapsed:g}", sw.Elapsed);
        result.Should().BeNull();

        sw.Restart();
        result = await locker.TryAcquireAsync(lockName, acquireTimeout: TimeSpan.FromSeconds(2.5));
        sw.Stop();
        Logger.LogInformation("Time to acquire lock: {Elapsed:g}", sw.Elapsed);
        result.Should().BeNull();
    }
}
