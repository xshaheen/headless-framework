// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheSlidingTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    // The HybridCache returned by _CreateCache is disposed per test via `await using`, but it does not own the
    // injected L1/L2 stores. This fixture collects those raw InMemoryCache instances and disposes them at teardown.
    private readonly List<object> _disposables = [];

    [Fact]
    public async Task should_store_sliding_metadata_in_l2_and_bound_local_copy()
    {
        // given
        var localExpiration = TimeSpan.FromMilliseconds(200);
        var duration = TimeSpan.FromSeconds(2);
        var slidingExpiration = TimeSpan.FromMilliseconds(800);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var (cache, l1, l2) = _CreateCache(new HybridCacheOptions { DefaultLocalExpiration = localExpiration });
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        var options = new CacheEntryOptions { Duration = duration, SlidingExpiration = slidingExpiration };

        // when
        var result = await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(value), options, AbortToken);

        // then
        var l1Entry = await ((IFactoryCacheStore)l1).TryGetEntryAsync<int>(key, cancellationToken: AbortToken);
        var l2Entry = await ((IFactoryCacheStore)l2).TryGetEntryAsync<int>(key, cancellationToken: AbortToken);

        result.Value.Should().Be(value);
        l2Entry.Value.Should().Be(value);
        l2Entry.LogicalExpiresAt.Should().Be(now.Add(slidingExpiration));
        l2Entry.PhysicalExpiresAt.Should().Be(now.Add(duration));
        l2Entry.SlidingExpiration.Should().Be(slidingExpiration);

        l1Entry.Value.Should().Be(value);
        l1Entry.LogicalExpiresAt.Should().Be(now.Add(localExpiration));
        l1Entry.PhysicalExpiresAt.Should().Be(now.Add(localExpiration));
        l1Entry.SlidingExpiration.Should().Be(slidingExpiration);
    }

    [Fact]
    public async Task should_rearm_sliding_l2_and_local_copy_without_shortening_l2_physical_cap()
    {
        // given
        var localExpiration = TimeSpan.FromSeconds(1);
        var duration = TimeSpan.FromSeconds(2);
        var slidingExpiration = TimeSpan.FromMilliseconds(400);
        var createdAt = _timeProvider.GetUtcNow().UtcDateTime;
        var (cache, l1, l2) = _CreateCache(new HybridCacheOptions { DefaultLocalExpiration = localExpiration });
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        var options = new CacheEntryOptions { Duration = duration, SlidingExpiration = slidingExpiration };

        await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(value), options, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(300));
        var rearmedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var factoryCalls = 0;

        // when
        var result = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                factoryCalls++;
                return new ValueTask<int?>(Faker.Random.Int());
            },
            options,
            AbortToken
        );

        // then
        var l1Entry = await ((IFactoryCacheStore)l1).TryGetEntryAsync<int>(key, cancellationToken: AbortToken);
        var l2Entry = await ((IFactoryCacheStore)l2).TryGetEntryAsync<int>(key, cancellationToken: AbortToken);

        result.Value.Should().Be(value);
        factoryCalls.Should().Be(0);
        l2Entry.Value.Should().Be(value);
        l2Entry.LogicalExpiresAt.Should().Be(rearmedAt.Add(slidingExpiration));
        l2Entry.PhysicalExpiresAt.Should().Be(createdAt.Add(duration));
        l2Entry.SlidingExpiration.Should().Be(slidingExpiration);

        l1Entry.Value.Should().Be(value);
        l1Entry.LogicalExpiresAt.Should().Be(rearmedAt.Add(slidingExpiration));
        l1Entry.PhysicalExpiresAt.Should().Be(rearmedAt.Add(localExpiration));
        l1Entry.SlidingExpiration.Should().Be(slidingExpiration);
    }

    [Fact]
    public async Task should_serve_sliding_l1_hit_when_l2_is_physically_expired()
    {
        // given - both tiers hold the sliding entry; L1's local ceiling outlives L2's absolute cap.
        var localExpiration = TimeSpan.FromSeconds(5);
        var duration = TimeSpan.FromSeconds(2);
        var slidingExpiration = TimeSpan.FromMilliseconds(500);
        var (cache, l1, l2) = _CreateCache(new HybridCacheOptions { DefaultLocalExpiration = localExpiration });
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        var options = new CacheEntryOptions { Duration = duration, SlidingExpiration = slidingExpiration };
        await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(value), options, AbortToken);

        // Simulate L2 physical expiry while L1 stays fresh: drop the L2 copy. A subsequent read is a fresh
        // sliding L1 hit that must fall through to the (now-empty) L2 and still serve the L1 value.
        (await l2.RemoveAsync(key, AbortToken))
            .Should()
            .BeTrue();

        var factoryCalls = 0;

        // when
        var result = await cache.GetOrAddAsync(
            key,
            _ =>
            {
                factoryCalls++;
                return new ValueTask<int?>(Faker.Random.Int());
            },
            options,
            AbortToken
        );

        // then - the value comes from L1 without re-invoking the factory, and the fallthrough does not
        // repopulate L2 (the served entry has its sliding metadata stripped, so no re-arm fires).
        result.Value.Should().Be(value);
        factoryCalls.Should().Be(0);

        var l1Entry = await ((IFactoryCacheStore)l1).TryGetEntryAsync<int>(key, cancellationToken: AbortToken);
        var l2Entry = await ((IFactoryCacheStore)l2).TryGetEntryAsync<int>(key, cancellationToken: AbortToken);
        l1Entry.Found.Should().BeTrue();
        l1Entry.Value.Should().Be(value);
        l2Entry.Found.Should().BeFalse();
    }

    private (HybridCache Cache, IInMemoryCache L1, IRemoteCache L2) _CreateCache(HybridCacheOptions options)
    {
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new InMemoryRemoteCacheAdapter(l2Inner);
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, options, timeProvider: _timeProvider);

        _disposables.Add(l1);
        _disposables.Add(l2Inner);

        return (cache, l1, l2);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var disposable in _disposables)
        {
            switch (disposable)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable syncDisposable:
                    syncDisposable.Dispose();
                    break;
            }
        }

        _disposables.Clear();
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }
}
