// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Verifies that an incoming Tag invalidation does NOT wipe the L1 entry for a key whose pending recovery item
/// was queued AFTER the tag-invalidation's timestamp (the local intent is newer and must win).
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
    public async Task should_not_wipe_l1_entry_when_tag_invalidation_is_older_than_queued_recovery_item()
    {
        // given — L2 is down so the factory write is queued for recovery; L1 holds the value with its tag
        var (cache, l1, l2, publisher) = _CreateCache();
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        var value = Faker.Random.Int();

        // The factory write timestamps the recovery item at "now" (t=0).
        var result = await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(value),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        result.Value.Should().Be(value);
        cache.RecoveryQueue!.Count.Should().Be(1, "L2 write failed — item is queued");
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeTrue("L1 holds the value");

        // when — a peer sends a Tag invalidation stamped BEFORE our recovery item (the peer's write lost the
        // race to our local write). The bug: without the fix the Tag branch calls RemoveByTagAsync without
        // consulting the queue, wiping L1; replay then finds nothing and drops itself as obsolete.
        var tagInvalidationTimestamp = _timeProvider.GetUtcNow().AddSeconds(-1);
        var message = new CacheInvalidationMessage
        {
            InstanceId = "instance-2",
            Tag = tag,
            Timestamp = tagInvalidationTimestamp,
        };

        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — the L1 entry must survive (recovery item is newer than the invalidation)
        (await l1.GetAsync<int>(key, AbortToken))
            .HasValue.Should()
            .BeTrue("L1 entry must not be wiped by a stale tag invalidation");

        // and — the recovery item itself must still be queued (replay must still be possible)
        cache.RecoveryQueue.Count.Should().Be(1, "recovery item must survive the stale tag invalidation");

        // when — L2 recovers and the replay pass runs
        l2.FailWrites = false;
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the value converges: L2 is populated and the queue is drained
        (await l2.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(value, "recovery replay must land the value in L2");

        cache.RecoveryQueue.Count.Should().Be(0, "queue must drain after successful replay");

        // and — the replay re-publishes the invalidation with the original write timestamp
        await publisher
            .Received()
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_remove_l1_entry_when_tag_invalidation_is_newer_than_queued_recovery_item()
    {
        // given — same setup: L2 is down, tagged value queued for recovery at t=0
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);

        await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(42),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        cache.RecoveryQueue!.Count.Should().Be(1);

        // when — a peer sends a Tag invalidation stamped AFTER our recovery item (the peer is genuinely newer)
        var message = new CacheInvalidationMessage
        {
            InstanceId = "instance-2",
            Tag = tag,
            Timestamp = _timeProvider.GetUtcNow().AddSeconds(1),
        };

        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — L1 must be wiped (the peer's newer write wins; our queued item is stale)
        (await l1.GetAsync<int>(key, AbortToken))
            .HasValue.Should()
            .BeFalse("a newer tag invalidation must remove the stale L1 entry");
    }

    [Fact]
    public async Task should_remove_l1_entries_without_recovery_items_even_when_sibling_key_is_protected()
    {
        // given — two keys share the same tag; only one has a queued recovery item
        var (cache, l1, l2, _) = _CreateCache();
        await using var _ = cache;

        var tag = Faker.Random.AlphaNumeric(8);
        var protectedKey = Faker.Random.AlphaNumeric(10);
        var unprotectedKey = Faker.Random.AlphaNumeric(10);
        var expiration = TimeSpan.FromMinutes(5);

        // Write both keys with the tag while L2 is up, so both land in L1 and L2
        await cache.GetOrAddAsync(
            protectedKey,
            _ => new ValueTask<int?>(1),
            new CacheEntryOptions { Duration = expiration, Tags = [tag] },
            AbortToken
        );
        await cache.GetOrAddAsync(
            unprotectedKey,
            _ => new ValueTask<int?>(2),
            new CacheEntryOptions { Duration = expiration, Tags = [tag] },
            AbortToken
        );

        // Now make L2 fail and re-write only the protected key so it queues a recovery item
        l2.FailWrites = true;
        await cache.UpsertEntryAsync(
            protectedKey,
            10,
            new CacheEntryOptions { Duration = expiration, Tags = [tag] },
            AbortToken
        );

        cache.RecoveryQueue!.Count.Should().Be(1, "only protectedKey has a queued item");
        (await l1.GetAsync<int>(protectedKey, AbortToken)).Value.Should().Be(10);
        (await l1.GetAsync<int>(unprotectedKey, AbortToken)).HasValue.Should().BeTrue();

        // when — a Tag invalidation arrives with an OLDER timestamp (older than protectedKey's recovery item)
        var message = new CacheInvalidationMessage
        {
            InstanceId = "instance-2",
            Tag = tag,
            Timestamp = _timeProvider.GetUtcNow().AddSeconds(-1),
        };

        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — the protected key's L1 entry survives (its recovery item is newer)
        (await l1.GetAsync<int>(protectedKey, AbortToken))
            .HasValue.Should()
            .BeTrue("protectedKey must survive: its recovery item is newer than the invalidation");

        // and — the unprotected key's L1 entry is removed (no recovery item guards it)
        (await l1.GetAsync<int>(unprotectedKey, AbortToken))
            .HasValue.Should()
            .BeFalse("unprotectedKey has no newer recovery item, so it must be removed");

        // and — the recovery item for protectedKey is still in the queue
        cache.RecoveryQueue.Count.Should().Be(1, "recovery item for protectedKey must survive");
    }
}
