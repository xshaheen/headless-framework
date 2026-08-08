// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheReadMatrixTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private (HybridCache cache, InMemoryCache l1, IRemoteCache l2) _CreateCache(HybridCacheOptions? options = null)
    {
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = Substitute.For<IRemoteCache>();

        return (
            new HybridCache(
                l1,
                l2,
                Substitute.For<IBus>(),
                options ?? new HybridCacheOptions(),
                timeProvider: _timeProvider
            ),
            l1,
            l2
        );
    }

    [Fact]
    public async Task should_return_l1_hit_without_reading_non_framed_l2()
    {
        var (cache, l1, l2) = _CreateCache();
        await using var _ = cache;
        using var __ = l1;
        const string key = "l1-hit";
        await l1.UpsertAsync(key, 7, TimeSpan.FromMinutes(5), AbortToken);

        var result = await cache.GetAsync<int>(key, AbortToken);

        result.Value.Should().Be(7);
        await l2.DidNotReceive().GetWithExpirationAsync<int>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_preserve_shorter_l2_expiration_when_promoting_non_framed_hit()
    {
        var (cache, l1, l2) = _CreateCache();
        await using var _ = cache;
        using var __ = l1;
        const string key = "l2-hit";
        var l2Expiration = TimeSpan.FromMinutes(2);
        l2.GetWithExpirationAsync<int>(key, Arg.Any<CancellationToken>())
            .Returns(new CacheValueWithExpiration<int>(new CacheValue<int>(42, hasValue: true), l2Expiration));

        var result = await cache.GetAsync<int>(key, AbortToken);

        result.Value.Should().Be(42);
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(42);
        (await l1.GetExpirationAsync(key, AbortToken)).Should().Be(l2Expiration);
    }

    [Fact]
    public async Task should_cap_non_framed_l2_expiration_when_promoting_to_l1()
    {
        var localExpiration = TimeSpan.FromMinutes(1);
        var (cache, l1, l2) = _CreateCache(new HybridCacheOptions { DefaultLocalExpiration = localExpiration });
        await using var _ = cache;
        using var __ = l1;
        const string key = "long-lived-l2-hit";
        l2.GetWithExpirationAsync<int>(key, Arg.Any<CancellationToken>())
            .Returns(
                new CacheValueWithExpiration<int>(new CacheValue<int>(42, hasValue: true), TimeSpan.FromMinutes(30))
            );

        var result = await cache.GetAsync<int>(key, AbortToken);

        result.Value.Should().Be(42);
        (await l1.GetExpirationAsync(key, AbortToken)).Should().Be(localExpiration);
    }

    [Fact]
    public async Task should_preserve_miss_when_non_framed_l2_has_no_value()
    {
        var (cache, l1, l2) = _CreateCache();
        await using var _ = cache;
        using var __ = l1;
        const string key = "missing";
        l2.GetWithExpirationAsync<int>(key, Arg.Any<CancellationToken>())
            .Returns(new CacheValueWithExpiration<int>(CacheValue<int>.NoValue, null));

        var result = await cache.GetAsync<int>(key, AbortToken);

        result.HasValue.Should().BeFalse();
        (await l1.ExistsAsync(key, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_merge_l1_hits_and_non_framed_l2_results_and_seed_only_hits()
    {
        var (cache, l1, l2) = _CreateCache();
        await using var _ = cache;
        using var __ = l1;
        var expiration = TimeSpan.FromMinutes(3);
        await l1.UpsertAsync("local", 1, expiration, AbortToken);
        l2.GetAllWithExpirationAsync<int>(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                new Dictionary<string, CacheValueWithExpiration<int>>(StringComparer.Ordinal)
                {
                    ["remote"] = new(new CacheValue<int>(2, hasValue: true), expiration),
                    ["missing"] = new(CacheValue<int>.NoValue, null),
                }
            );

        var result = await cache.GetAllAsync<int>(["local", "remote", "missing"], AbortToken);

        result["local"].Value.Should().Be(1);
        result["remote"].Value.Should().Be(2);
        result["missing"].HasValue.Should().BeFalse();
        (await l1.GetAsync<int>("remote", AbortToken)).Value.Should().Be(2);
        (await l1.ExistsAsync("missing", AbortToken)).Should().BeFalse();
        await l2.Received(1)
            .GetAllWithExpirationAsync<int>(
                Arg.Is<IEnumerable<string>>(keys => keys.Order().SequenceEqual(new[] { "missing", "remote" })),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_short_circuit_exists_on_l1_hit()
    {
        var (cache, l1, l2) = _CreateCache();
        await using var _ = cache;
        using var __ = l1;
        const string key = "exists";
        await l1.UpsertAsync(key, 1, TimeSpan.FromMinutes(5), AbortToken);

        (await cache.ExistsAsync(key, AbortToken)).Should().BeTrue();

        await l2.DidNotReceive().ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        cache.LocalCacheHits.Should().Be(1);
    }

    [Fact]
    public async Task should_fall_back_to_l2_for_exists_and_expiration_misses()
    {
        var (cache, l1, l2) = _CreateCache();
        await using var _ = cache;
        using var __ = l1;
        const string key = "remote";
        var expiration = TimeSpan.FromMinutes(4);
        l2.ExistsAsync(key, Arg.Any<CancellationToken>()).Returns(true);
        l2.GetExpirationAsync(key, Arg.Any<CancellationToken>()).Returns(expiration);

        (await cache.ExistsAsync(key, AbortToken)).Should().BeTrue();
        (await cache.GetExpirationAsync(key, AbortToken)).Should().Be(expiration);
    }

    [Fact]
    public async Task should_propagate_cancellation_during_l2_exists_read()
    {
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(_timeProvider)
        {
            ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var cache = new HybridCache(
            l1,
            l2,
            Substitute.For<IBus>(),
            new HybridCacheOptions(),
            timeProvider: _timeProvider
        );
        await using var _ = cache;
        using var cancellation = new CancellationTokenSource();

        Func<Task<bool>> act = () => cache.ExistsAsync("slow", cancellation.Token).AsTask();
        var assertion = act.Should().ThrowAsync<OperationCanceledException>();

        await l2.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cancellation.CancelAsync();
        await assertion;
    }
}
