// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheAutoRecoveryTests : TestBase
{
    private static readonly TimeSpan _Delay = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _timeProvider = new();

    private (HybridCache cache, InMemoryCache l1, TogglableRemoteCache l2, IBus publisher) _CreateCache(
        HybridCacheOptions? options = null,
        IBus? publisher = null
    )
    {
        options ??= new HybridCacheOptions();
        var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new TogglableRemoteCache(_timeProvider);

        if (publisher is null)
        {
            publisher = Substitute.For<IBus>();
            publisher
                .PublishAsync(
                    Arg.Any<CacheInvalidationMessage>(),
                    Arg.Any<PublishOptions?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Task.CompletedTask);
        }

        var cache = new HybridCache(l1, l2, publisher, options, timeProvider: _timeProvider);

        return (cache, l1, l2, publisher);
    }

    [Fact]
    public async Task should_propagate_l2_write_failure_when_auto_recovery_disabled()
    {
        // given — default options: auto-recovery off
        var (cache, _, l2, _) = _CreateCache();
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var act = async () => await cache.UpsertAsync(key, Faker.Random.Int(), TimeSpan.FromMinutes(5), AbortToken);

        // then — exactly today's behavior: the scalar L2 failure surfaces to the caller, no queue exists
        await act.Should().ThrowAsync<InvalidOperationException>();
        cache.RecoveryQueue.Should().BeNull();
    }

    [Fact]
    public async Task should_queue_failed_factory_write_and_replay_when_l2_recovers()
    {
        // given
        var (cache, l1, l2, _) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true });
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when — the factory write fails against L2 but the caller still succeeds
        var result = await cache.GetOrAddAsync(
            key,
            _ => new ValueTask<int?>(value),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then — degraded mode: L1 has the value, the L2 write is queued
        result.Value.Should().Be(value);
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
        cache.RecoveryQueue!.Count.Should().Be(1);

        // when — L2 recovers and the processing cadence elapses
        l2.FailWrites = false;
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the replay landed the value in L2 and the queue drained
        (await l2.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(value);
        cache.RecoveryQueue.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_drop_queued_set_when_l1_entry_changed_before_replay()
    {
        // given — a queued Set whose L1 entry is replaced (different physical stamp) before replay
        var (cache, l1, l2, _) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true });
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(1), TimeSpan.FromMinutes(5), AbortToken);
        cache.RecoveryQueue!.Count.Should().Be(1);
        var failedAttempts = l2.SetEntryAttempts;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await ((IFactoryCacheStore)l1).SetEntryAsync(
            key,
            new CacheStoreEntryWrite<int>
            {
                Value = 2,
                IsNull = false,
                LogicalExpiresAt = now.AddMinutes(10),
                PhysicalExpiresAt = now.AddMinutes(10),
            },
            AbortToken
        );

        // when — L2 recovers and the pass runs
        l2.FailWrites = false;
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the obsolete item was dropped silently; the stale value was NOT written to L2
        cache.RecoveryQueue.Count.Should().Be(0);
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
        l2.SetEntryAttempts.Should().Be(failedAttempts, "the obsolete item must not be replayed against L2");
    }

    [Fact]
    public async Task should_not_attempt_replay_again_before_barrier_elapses()
    {
        // given — two queued items, L2 stays down
        var (cache, _, l2, _) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true });
        await using var _ = cache;

        l2.FailWrites = true;
        await cache.GetOrAddAsync("key-1", _ => new ValueTask<int?>(1), TimeSpan.FromMinutes(5), AbortToken);
        await cache.GetOrAddAsync("key-2", _ => new ValueTask<int?>(2), TimeSpan.FromMinutes(5), AbortToken);
        l2.SetEntryAttempts.Should().Be(2);

        // when — the first pass runs: it must stop at the first failure (single attempt, not two)
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue!.ProcessAsync(AbortToken);
        l2.SetEntryAttempts.Should().Be(3, "the pass stops at the first replay failure");

        // and — another pass before the barrier elapses must not attempt anything
        await cache.RecoveryQueue.ProcessAsync(AbortToken);
        l2.SetEntryAttempts.Should().Be(3, "no replay attempt is allowed before the barrier elapses");

        // and — once the barrier elapses, replay is attempted again
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);
        l2.SetEntryAttempts.Should().Be(4);
    }

    [Fact]
    public async Task should_drop_item_after_max_retries_and_keep_processing()
    {
        // given — L2 stays down and the retry budget is 2
        var (cache, _, l2, _) = _CreateCache(
            new HybridCacheOptions { EnableAutoRecovery = true, AutoRecoveryMaxRetries = 2 }
        );
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(1), TimeSpan.FromMinutes(5), AbortToken);
        cache.RecoveryQueue!.Count.Should().Be(1);

        // when — two failed replay passes exhaust the retry budget
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the item is dropped
        cache.RecoveryQueue.Count.Should().Be(0);

        // and — the loop keeps processing newer items
        var otherKey = Faker.Random.AlphaNumeric(10);
        var otherValue = Faker.Random.Int();
        await cache.UpsertAsync(otherKey, otherValue, TimeSpan.FromMinutes(5), AbortToken);
        cache.RecoveryQueue.Count.Should().Be(1);

        l2.FailWrites = false;
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        cache.RecoveryQueue.Count.Should().Be(0);
        (await l2.GetAsync<int>(otherKey, AbortToken)).Value.Should().Be(otherValue);
    }

    [Fact]
    public async Task should_evict_earliest_expiry_item_when_queue_full()
    {
        // given — capacity for two items
        var (cache, _, l2, _) = _CreateCache(
            new HybridCacheOptions { EnableAutoRecovery = true, AutoRecoveryMaxItems = 2 }
        );
        await using var _ = cache;

        l2.FailWrites = true;

        // when — three failed writes with distinct expirations
        await cache.GetOrAddAsync("key-10m", _ => new ValueTask<int?>(1), TimeSpan.FromMinutes(10), AbortToken);
        await cache.GetOrAddAsync("key-5m", _ => new ValueTask<int?>(2), TimeSpan.FromMinutes(5), AbortToken);
        await cache.GetOrAddAsync("key-15m", _ => new ValueTask<int?>(3), TimeSpan.FromMinutes(15), AbortToken);

        // then — the item with the earliest expiry was evicted to admit the new one
        cache.RecoveryQueue!.Count.Should().Be(2);
        cache.RecoveryQueue.Contains("key-10m").Should().BeTrue();
        cache.RecoveryQueue.Contains("key-5m").Should().BeFalse("the earliest-expiring item is the eviction victim");
        cache.RecoveryQueue.Contains("key-15m").Should().BeTrue();
    }

    [Fact]
    public async Task should_drop_queued_item_when_newer_foreign_invalidation_arrives()
    {
        // given — a queued Set for the key
        var (cache, l1, l2, _) = _CreateCache(
            new HybridCacheOptions { EnableAutoRecovery = true, InstanceId = "instance-1" }
        );
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        await cache.GetOrAddAsync(key, _ => new ValueTask<int?>(1), TimeSpan.FromMinutes(5), AbortToken);
        cache.RecoveryQueue!.Count.Should().Be(1);

        // when — another node invalidates the key with a newer timestamp
        var message = new CacheInvalidationMessage
        {
            InstanceId = "instance-2",
            Key = key,
            Timestamp = _timeProvider.GetUtcNow().AddSeconds(1),
        };

        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — the queued item is dropped (the other node won) and L1 is invalidated as usual
        cache.RecoveryQueue.Count.Should().Be(0);
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_queue_failed_publish_and_republish_on_recovery()
    {
        // given — L2 healthy, the invalidation backplane down for the first publish
        var failPublish = true;
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => failPublish ? throw new InvalidOperationException("Publish failed") : Task.CompletedTask);

        var (cache, _, l2, _) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true }, publisher);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when — the write succeeds but the publish fails
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then — the value landed in L2 and the publish was queued
        (await l2.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(value);
        cache.RecoveryQueue!.Count.Should().Be(1);

        // when — the backplane recovers and the cadence elapses
        failPublish = false;
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the captured message was re-published
        cache.RecoveryQueue.Count.Should().Be(0);
        await publisher
            .Received(2)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_queue_failed_remove_and_replay_when_l2_recovers()
    {
        // given — the key exists in both tiers
        var (cache, l1, l2, _) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true });
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        await l1.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);
        await l2.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // when — the L2 remove fails
        l2.FailWrites = true;
        var removed = await cache.RemoveAsync(key, AbortToken);

        // then — the caller succeeds, L1 is removed, the L2 removal is queued
        removed.Should().BeTrue();
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeTrue("the L2 removal is still pending");
        cache.RecoveryQueue!.Count.Should().Be(1);

        // when — L2 recovers and the cadence elapses
        l2.FailWrites = false;
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the removal was replayed
        cache.RecoveryQueue.Count.Should().Be(0);
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
    }
}
