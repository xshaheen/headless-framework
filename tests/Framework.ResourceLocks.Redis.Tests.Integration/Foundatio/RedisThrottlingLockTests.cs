using System.Diagnostics;
using Foundatio.Caching;
using Foundatio.Lock;
using Framework.Caching;
using Framework.Testing.Tests;
using Framework.Threading;
using Microsoft.Extensions.Logging;

namespace Tests.Foundatio;

public sealed class RedisThrottlingLockTests : ThrottlingLockTestBase
{
    private readonly RedisCacheClient _cache;

    public RedisThrottlingLockTests(ITestOutputHelper output)
        : base(output)
    {
        var muxer = SharedConnection.GetMuxer(LoggerFactory);
        Async.RunSync(muxer.FlushAllAsync);
        _cache = new(o => o.ConnectionMultiplexer(muxer).LoggerFactory(LoggerFactory));
    }

    protected override ILockProvider GetLockProvider(int maxHits, TimeSpan period)
    {
        return new ThrottlingLockProvider(_cache, maxHits, period, null, LoggerFactory);
    }

    [Fact]
    public override Task WillThrottleCallsAsync()
    {
        return base.WillThrottleCallsAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _cache?.Dispose();
        }
    }
}

public abstract class ThrottlingLockTestBase(ITestOutputHelper output) : TestBase(output)
{
    protected abstract ILockProvider GetLockProvider(int maxHits, TimeSpan period);

    public virtual async Task WillThrottleCallsAsync()
    {
        const int allowedLocks = 25;

        var period = TimeSpan.FromSeconds(2);
        var locker = GetLockProvider(allowedLocks, period);

        if (locker == null)
        {
            return;
        }

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
            var l = await locker.AcquireAsync(lockName);
            l.Should().NotBeNull();
        }

        sw.Stop();

        Logger.LogInformation("Time to acquire {AllowedLocks} locks: {Elapsed:g}", allowedLocks, sw.Elapsed);
        (sw.Elapsed.TotalSeconds < 1).Should().BeTrue();

        sw.Restart();
        var result = await locker.AcquireAsync(lockName, cancellationToken: new CancellationToken(true));
        sw.Stop();
        Logger.LogInformation("Total acquire time took to attempt to get throttled lock: {Elapsed:g}", sw.Elapsed);
        result.Should().BeNull();

        sw.Restart();
        result = await locker.AcquireAsync(lockName, acquireTimeout: TimeSpan.FromSeconds(2.5));
        sw.Stop();
        Logger.LogInformation("Time to acquire lock: {Elapsed:g}", sw.Elapsed);
        result.Should().NotBeNull();
    }
}
