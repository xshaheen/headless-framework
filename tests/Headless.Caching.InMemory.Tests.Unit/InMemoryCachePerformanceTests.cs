// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryCachePerformanceTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private InMemoryCache _CreateCache(InMemoryCacheOptions? options = null)
    {
        options ??= new InMemoryCacheOptions();
        return new InMemoryCache(_timeProvider, options);
    }

    // Acceptance gate: sliding re-arm must be throttled, never unconditional. A hot key read many times
    // within the first half of its idle window must NOT be re-written on every read (which would hammer the
    // store under load); the logical deadline only advances once at least half the window has elapsed.
    [Fact]
    public async Task sliding_rearm_is_throttled_on_a_hot_key_and_does_not_rewrite_on_every_read()
    {
        // given - a sliding entry whose idle window (1s) is well inside the absolute cap (30s)
        using var cache = _CreateCache();
        var store = (IFactoryCacheStore)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var sliding = TimeSpan.FromSeconds(1);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var options = new CacheEntryOptions { Duration = TimeSpan.FromSeconds(30), SlidingExpiration = sliding };
        await cache.GetOrAddAsync(key, _ => new ValueTask<string?>("value"), options, AbortToken);

        // when - a burst of hot reads with no time advance (more than half the window still remains)
        for (var i = 0; i < 1_000; i++)
        {
            (await cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("value");
        }

        // then - the throttle suppressed every re-arm: the logical deadline is unchanged from the initial set.
        // TryGetEntryAsync is a non-re-arming inspection read, so it does not perturb the measurement.
        var afterBurst = await store.TryGetEntryAsync<string>(key, cancellationToken: AbortToken);
        afterBurst.LogicalExpiresAt.Should().Be(now.Add(sliding));

        // and when - more than half the idle window elapses, the next read re-arms exactly once...
        _timeProvider.Advance(TimeSpan.FromMilliseconds(600));
        var rearmAt = _timeProvider.GetUtcNow().UtcDateTime;
        (await cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("value");

        var afterThreshold = await store.TryGetEntryAsync<string>(key, cancellationToken: AbortToken);
        afterThreshold.LogicalExpiresAt.Should().Be(rearmAt.Add(sliding));

        // ...and an immediate follow-up read is throttled again (back above the half-window).
        (await cache.GetAsync<string>(key, AbortToken))
            .Value.Should()
            .Be("value");
        var afterSecond = await store.TryGetEntryAsync<string>(key, cancellationToken: AbortToken);
        afterSecond.LogicalExpiresAt.Should().Be(rearmAt.Add(sliding));
    }
}
