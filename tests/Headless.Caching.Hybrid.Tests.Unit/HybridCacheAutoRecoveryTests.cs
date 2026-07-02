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
    public async Task should_propagate_bulk_l2_upsert_failure()
    {
        // given — bulk ops are not captured by auto-recovery; the L2 failure must surface, not be swallowed
        var (cache, _, l2, _) = _CreateCache();
        await using var _ = cache;

        l2.FailWrites = true;
        var values = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 };

        // when
        var act = async () => await cache.UpsertAllAsync(values, TimeSpan.FromMinutes(5), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_propagate_bulk_l2_remove_failure()
    {
        // given
        var (cache, _, l2, _) = _CreateCache();
        await using var _ = cache;

        l2.FailWrites = true;

        // when
        var act = async () => await cache.RemoveAllAsync(["a", "b"], AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_queue_failed_factory_write_and_replay_when_l2_recovers()
    {
        // given
        var (cache, l1, l2, publisher) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true });
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

        // then — the replay landed the value in L2 and the queue drained, and the replayed entry write
        // republished the key invalidation (live publish + post-replay publish)
        (await l2.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(value);
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

        // when — the first pass runs: it attempts every queued item (continue-on-failure), so both items
        // are replayed in one pass — a single poison item must not starve the rest of the queue.
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue!.ProcessAsync(AbortToken);
        l2.SetEntryAttempts.Should().Be(4, "the pass attempts every queued item, not just the first");

        // and — another pass before the barrier elapses must not attempt anything
        await cache.RecoveryQueue.ProcessAsync(AbortToken);
        l2.SetEntryAttempts.Should().Be(4, "no replay attempt is allowed before the barrier elapses");

        // and — once the barrier elapses, both items are attempted again
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);
        l2.SetEntryAttempts.Should().Be(6);
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
        var (cache, l1, l2, publisher) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true });
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

        // then — the removal was replayed and republished the key invalidation (live publish + post-replay)
        cache.RecoveryQueue.Count.Should().Be(0);
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
        await publisher
            .Received(2)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_displace_queued_value_op_when_publish_fails_for_same_key()
    {
        // given — a full outage: L2 writes and invalidation publishes both fail
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("Publish failed"));

        var (cache, _, l2, _) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true }, publisher);
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();

        // when — the L2 write fails (queues a SetEntry), then the publish fails for the same key
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then — the value op survives: the failed publish is subsumed instead of displacing it
        cache.RecoveryQueue!.Count.Should().Be(1);
        cache.RecoveryQueue.GetKind(key).Should().Be(HybridCacheRecoveryKind.SetEntry);
    }

    [Fact]
    public async Task should_replace_queued_publish_when_value_op_queued_for_same_key()
    {
        // given — a backplane-only outage queues a publish for the key
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("Publish failed"));

        var (cache, _, l2, _) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true }, publisher);
        await using var _ = cache;

        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, Faker.Random.Int(), TimeSpan.FromMinutes(5), AbortToken);
        cache.RecoveryQueue!.GetKind(key).Should().Be(HybridCacheRecoveryKind.PublishInvalidation);

        // when — a later write to the same key fails against L2 too
        l2.FailWrites = true;
        await cache.UpsertAsync(key, Faker.Random.Int(), TimeSpan.FromMinutes(5), AbortToken);

        // then — last intent wins: the value op replaced the queued publish (which it subsumes)
        cache.RecoveryQueue.Count.Should().Be(1);
        cache.RecoveryQueue.GetKind(key).Should().Be(HybridCacheRecoveryKind.SetEntry);
    }

    [Fact]
    public async Task should_publish_invalidation_with_original_write_timestamp_after_successful_set_replay()
    {
        // given — L2 down, backplane healthy
        var (cache, _, l2, publisher) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true });
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        var writeTime = _timeProvider.GetUtcNow();

        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);
        cache.RecoveryQueue!.GetKind(key).Should().Be(HybridCacheRecoveryKind.SetEntry);

        // when — L2 recovers and the replay pass runs after the cadence elapsed
        l2.FailWrites = false;
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the value landed in L2 and the replay republished the key invalidation stamped with the
        // ORIGINAL write time (not the replay time), so receivers order it correctly against newer writes
        cache.RecoveryQueue.Count.Should().Be(0);
        (await l2.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
        await publisher
            .Received(2)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key && m.Timestamp == writeTime),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_queue_residual_publish_when_post_replay_publish_fails()
    {
        // given — a full outage queues a SetEntry for the key
        var failPublish = true;
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => failPublish ? throw new InvalidOperationException("Publish failed") : Task.CompletedTask);

        var (cache, _, l2, _) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true }, publisher);
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);
        cache.RecoveryQueue!.GetKind(key).Should().Be(HybridCacheRecoveryKind.SetEntry);

        // when — only L2 recovers; the replay pass lands the value but its post-replay publish fails
        l2.FailWrites = false;
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the Set already landed, so the correct residual intent is a queued PublishInvalidation
        (await l2.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(value);
        cache.RecoveryQueue.Count.Should().Be(1);
        cache.RecoveryQueue.GetKind(key).Should().Be(HybridCacheRecoveryKind.PublishInvalidation);

        // when — the backplane recovers and the next pass runs
        failPublish = false;
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the residual invalidation went out and the queue drained (initial failed publish +
        // post-replay failed publish + successful residual replay)
        cache.RecoveryQueue.Count.Should().Be(0);
        await publisher
            .Received(3)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Key == key),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_bound_residual_publish_retries_when_backplane_stays_down()
    {
        // given — a full outage with a tight retry budget; the backplane never recovers
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("Publish failed"));

        var (cache, _, l2, _) = _CreateCache(
            new HybridCacheOptions { EnableAutoRecovery = true, AutoRecoveryMaxRetries = 2 },
            publisher
        );
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, Faker.Random.Int(), TimeSpan.FromMinutes(5), AbortToken);

        // when — L2 recovers: the Set replays and its failed post-replay publish queues the residual
        l2.FailWrites = false;
        await cache.RecoveryQueue!.ProcessAsync(AbortToken);
        cache.RecoveryQueue.GetKind(key).Should().Be(HybridCacheRecoveryKind.PublishInvalidation);

        // and — every further pass keeps failing until the residual exhausts its budget (one attempt per
        // pass, separated by the failure barrier)
        await cache.RecoveryQueue.ProcessAsync(AbortToken);
        cache.RecoveryQueue.Count.Should().Be(1, "one retry left in the budget");
        _timeProvider.Advance(_Delay);

        // then — the residual cannot loop unboundedly: it is gone and no further publish attempts are made
        cache.RecoveryQueue.Count.Should().Be(0);
        var publishAttempts = publisher.ReceivedCalls().Count();
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);
        publisher.ReceivedCalls().Should().HaveCount(publishAttempts, "the dropped residual must not be retried");
    }

    [Fact]
    public async Task should_ignore_older_foreign_invalidation_when_newer_local_op_is_queued()
    {
        // given — a queued Set for the key (our local intent)
        var (cache, l1, l2, _) = _CreateCache(
            new HybridCacheOptions { EnableAutoRecovery = true, InstanceId = "instance-1" }
        );
        await using var _ = cache;

        l2.FailWrites = true;
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Random.Int();
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);
        cache.RecoveryQueue!.Count.Should().Be(1);

        // when — another node's invalidation arrives with an OLDER timestamp (it lost the race to our write)
        var message = new CacheInvalidationMessage
        {
            InstanceId = "instance-2",
            Key = key,
            Timestamp = _timeProvider.GetUtcNow().AddSeconds(-1),
        };

        await cache.HandleInvalidationAsync(message, AbortToken);

        // then — our newer pending write survives AND keeps its L1 entry: wiping L1 would make the
        // stamp-verified replay drop itself as obsolete and lose the newer value
        cache.RecoveryQueue.Count.Should().Be(1);
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(value);
    }

    [Fact]
    public async Task should_queue_failed_tag_marker_bump_and_replay_when_l2_recovers()
    {
        // given — an entry present in both tiers, tagged
        var (cache, l1, l2, publisher) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true });
        await using var _ = cache;
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        await cache.UpsertEntryAsync(
            key,
            42,
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] },
            AbortToken
        );

        // when — tag invalidation while the L2 marker bump fails (advance so the marker postdates the write)
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        l2.FailMarkerBumps = true;
        await cache.RemoveByTagAsync(tag, AbortToken);

        // then — L1 invalidated locally (best-effort did not throw), the marker bump is queued, and the L2 marker
        // was NOT written, so an L2 read still serves the entry (the gap this feature closes)
        cache.RecoveryQueue!.Count.Should().Be(1);
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeTrue();

        // when — L2 recovers and the recovery pass runs
        l2.FailMarkerBumps = false;
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the replay wrote the L2 marker; the L2 read now misses and the queue drained
        cache.RecoveryQueue.Count.Should().Be(0);
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();

        // and — the tag invalidation was re-broadcast on replay (live publish + replay publish)
        await publisher
            .Received(2)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(m => m.Tag == tag),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_queue_marker_bump_without_attempting_l2_when_circuit_is_open()
    {
        // given — the distributed circuit is already open from a prior L2 read failure
        var (cache, _, l2, _) = _CreateCache(
            new HybridCacheOptions
            {
                EnableAutoRecovery = true,
                DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(30),
            }
        );
        await using var _ = cache;

        var primer = Faker.Random.AlphaNumeric(10);
        await l2.UpsertAsync(primer, 1, TimeSpan.FromMinutes(5), AbortToken);
        l2.FailReads = true;
        await cache.GetAsync<int>(primer, AbortToken); // opens the circuit
        l2.FailReads = false;

        var attemptsBefore = l2.RemoveAttempts;

        // when — a tag invalidation runs while the circuit is open; the marker bump must be queued, not attempted
        await cache.RemoveByTagAsync(Faker.Random.AlphaNumeric(8), AbortToken);

        // then — queued for replay; no live L2 marker write was attempted (FailMarkerBumps was never set, yet the
        // circuit-open path skipped the live write entirely)
        cache.RecoveryQueue!.Count.Should().Be(1);
        l2.RemoveAttempts.Should().Be(attemptsBefore);
    }

    [Fact]
    public async Task should_queue_failed_clear_marker_bump_and_replay_when_l2_recovers()
    {
        // given — an entry present in both tiers
        var (cache, l1, l2, _) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true });
        await using var _ = cache;
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertEntryAsync(key, 42, new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) }, AbortToken);

        // when — a logical clear while the L2 marker bump fails
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        l2.FailMarkerBumps = true;
        await cache.ClearAsync(AbortToken);

        // then — L1 cleared locally, the bump is queued, and the L2 clear marker was not written
        cache.RecoveryQueue!.Count.Should().Be(1);
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeTrue();

        // when — L2 recovers and the recovery pass runs
        l2.FailMarkerBumps = false;
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the replay wrote the L2 clear marker; the L2 read now misses and the queue drained
        cache.RecoveryQueue.Count.Should().Be(0);
        (await l2.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_replay_tag_marker_at_original_timestamp_so_a_later_write_survives()
    {
        // given
        var (cache, _, l2, _) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true });
        await using var _ = cache;
        var key = Faker.Random.AlphaNumeric(10);
        var tag = Faker.Random.AlphaNumeric(8);
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] };
        await cache.UpsertEntryAsync(key, 1, options, AbortToken);

        // when — tag invalidation fails its L2 marker bump (queued at this instant)
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        l2.FailMarkerBumps = true;
        await cache.RemoveByTagAsync(tag, AbortToken);

        // ...and a newer write for the same key/tag lands during the outage (only marker bumps fail here)
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        await cache.UpsertEntryAsync(key, 2, options, AbortToken);

        // when — recover and replay the queued marker (re-asserts the ORIGINAL timestamp, raise-only)
        l2.FailMarkerBumps = false;
        _timeProvider.Advance(_Delay);
        await cache.RecoveryQueue!.ProcessAsync(AbortToken);

        // then — the newer write survives on L2: replaying the marker at its original (older) timestamp does not
        // invalidate an entry born after it (would fail if the replay stamped the recovery-time clock)
        (await l2.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(2);
    }

    [Fact]
    public async Task should_not_drop_queued_marker_bump_on_incoming_flush_all()
    {
        // given — a queued tag marker bump
        var (cache, _, l2, _) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true });
        await using var _ = cache;
        var tag = Faker.Random.AlphaNumeric(8);
        l2.FailMarkerBumps = true;
        await cache.RemoveByTagAsync(tag, AbortToken);
        cache.RecoveryQueue!.Count.Should().Be(1);

        // when — a foreign FlushAll arrives with a strictly newer timestamp
        await cache.HandleInvalidationAsync(
            new CacheInvalidationMessage
            {
                InstanceId = "other",
                FlushAll = true,
                Timestamp = _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(1),
            },
            AbortToken
        );

        // then — the marker bump is raise-only/idempotent and exempt from the conflict drop, so it survives
        cache.RecoveryQueue.Count.Should().Be(1);
    }

    [Fact]
    public async Task should_coalesce_repeated_marker_bumps_for_the_same_tag()
    {
        // given
        var (cache, _, l2, _) = _CreateCache(new HybridCacheOptions { EnableAutoRecovery = true });
        await using var _ = cache;
        var tag = Faker.Random.AlphaNumeric(8);
        l2.FailMarkerBumps = true;

        // when — two failed bumps for the same tag under the outage
        await cache.RemoveByTagAsync(tag, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await cache.RemoveByTagAsync(tag, AbortToken);

        // then — they share one synthetic key, so the queue holds a single coalesced item (last intent wins)
        cache.RecoveryQueue!.Count.Should().Be(1);
    }
}
