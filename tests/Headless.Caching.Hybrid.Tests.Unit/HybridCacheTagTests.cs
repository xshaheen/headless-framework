// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheTagTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private (HybridCache cache, IInMemoryCache l1, IRemoteCache l2, IBus publisher) _CreateCache(
        HybridCacheOptions? options = null
    )
    {
        options ??= new HybridCacheOptions();
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        var l1 = new InMemoryCache(_timeProvider, l1Options);

        // Create a separate in-memory cache as the "distributed" cache for testing
        var l2Options = new InMemoryCacheOptions { CloneValues = true };
        var l2 = new InMemoryRemoteCacheAdapter(new InMemoryCache(_timeProvider, l2Options));

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, options, timeProvider: _timeProvider);

        return (cache, l1, l2, publisher);
    }

    [Fact]
    public async Task should_publish_tag_invalidation_when_removing_by_tag()
    {
        // given
        var (cache, _, _, publisher) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("value"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // when
        var removed = await cache.RemoveByTagAsync(tag, AbortToken);

        // then — the invalidation is published with the tag, and the L2 count is returned
        removed.Should().Be(1);
        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Tag == tag && m.Key == null && !m.FlushAll),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_remove_both_tiers_when_removing_by_tag()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("value"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // when
        await cache.RemoveByTagAsync(tag, AbortToken);

        // then
        (await l1.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();
        (await l2.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_remove_matching_l1_entries_when_tag_invalidation_received()
    {
        // given — the entry was promoted into L1 with its tags via the envelope
        var options = new HybridCacheOptions { InstanceId = "instance-1" };
        var (cache, l1, _, _) = _CreateCache(options);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("value"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        var message = new CacheInvalidationMessage
        {
            InstanceId = "instance-2", // Different instance
            Tag = tag,
        };

        // when
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — only the local tier is invalidated by the consumer path
        (await l1.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();
        cache.InvalidateCacheCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_ignore_self_originated_tag_invalidation()
    {
        // given
        var options = new HybridCacheOptions { InstanceId = "instance-1" };
        var (cache, l1, _, _) = _CreateCache(options);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<string?>("value"),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        var message = new CacheInvalidationMessage { InstanceId = "instance-1", Tag = tag };

        // when
        await cache.HandleInvalidationAsync(message, AbortToken);

        // then
        (await l1.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeTrue();
        cache.InvalidateCacheCalls.Should().Be(0);
    }
}
