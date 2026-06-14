// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Family-2 tag invalidation is a logical O(1) marker bump, not an eager per-key wipe. These tests verify that
/// version-pinning (entry birth time vs marker) interacts correctly with the auto-recovery queue: a tag
/// invalidation does not delete the L1 entry (so the queued recovery item is never orphaned), and recovery
/// replay converges by re-writing the value with a fresh birth time that survives the earlier marker.
/// </summary>
public sealed class HybridCacheTagAutoRecoveryTests : TestBase
{
    private static readonly TimeSpan _Delay = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _timeProvider = new();

    private (HybridCache cache, InMemoryCache l1, TogglableRemoteCache l2, IBus publisher) _CreateCache(
        HybridCacheOptions? options = null
    )
    {
        options ??= new HybridCacheOptions { EnableAutoRecovery = true, InstanceId = "instance-1" };
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new TogglableRemoteCache(_timeProvider);

        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, options, timeProvider: _timeProvider);

        return (cache, l1, l2, publisher);
    }

    [Fact]
    public async Task should_keep_recovery_item_queued_through_a_tag_invalidation()
    {
        // given — L2 is down so the factory write is queued for recovery; L1 holds the value with its tag
        var (cache, _, l2, publisher) = _CreateCache();
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        var value = Faker.Random.Int();

        var result = await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(value),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        result.Value.Should().Be(value);
        cache.RecoveryQueue!.Count.Should().Be(1, "L2 write failed — item is queued");

        // when — a peer sends a Tag invalidation. The logical marker bump never deletes the L1 entry, so the
        // queued recovery item is not orphaned regardless of ordering.
        var message = new CacheInvalidationMessage
        {
            InstanceId = "instance-2",
            Tag = tag,
            Timestamp = _timeProvider.GetUtcNow(),
        };

        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — the recovery item must still be queued (replay must still be possible)
        cache.RecoveryQueue.Count.Should().Be(1, "the recovery item must survive a tag invalidation");

        // when — L2 recovers and the replay pass runs (the replay re-writes with a fresh birth time)
        l2.FailWrites = false;
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the value converges: L2 is populated and the queue is drained
        (await l2.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(value, "recovery replay must land the value in L2");
        cache.RecoveryQueue.Count.Should().Be(0, "queue must drain after successful replay");

        // and — the replay re-publishes the invalidation for the key
        await publisher
            .Received()
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_invalidate_tagged_l1_entry_on_received_tag_invalidation()
    {
        // given — a tagged value present in L1
        var (cache, l1, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(42),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // when — a peer sends a Tag invalidation; the receiver bumps its L1 marker (postdating the entry).
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        var message = new CacheInvalidationMessage
        {
            InstanceId = "instance-2",
            Tag = tag,
            Timestamp = _timeProvider.GetUtcNow(),
        };

        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — the tagged L1 entry reads as a miss.
        (await l1.GetAsync<int>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_not_invalidate_l1_entry_recreated_after_tag_marker()
    {
        // given — a tagged value present in L1
        var (cache, l1, _, _) = _CreateCache();
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(1),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // when — a tag invalidation arrives, then the key is re-created locally with a fresh birth time.
        await cache.HandleInvalidationAsync(
            new CacheInvalidationMessage
            {
                InstanceId = "instance-2",
                Tag = tag,
                Timestamp = _timeProvider.GetUtcNow(),
            },
            AbortToken
        );

        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.UpsertEntryAsync(
            key,
            2,
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // then — version-pinning: the re-created entry survives the earlier tag marker.
        var cached = await l1.GetAsync<int>(key, AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be(2);
    }
}
