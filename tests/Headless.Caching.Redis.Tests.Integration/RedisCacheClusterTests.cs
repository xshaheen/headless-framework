// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Serializer;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// The bulk operations split their keys by hash slot only on a cluster, and run the per-slot commands with a bounded
/// number in flight. These tests drive them against a three-primary cluster with keys that span far more slots than
/// that bound, so every wave of slot commands runs, and take one slot away to check that the others still complete.
/// </summary>
[Collection(nameof(RedisClusterCacheFixture))]
public sealed class RedisCacheClusterTests(RedisClusterCacheFixture fixture) : TestBase
{
    // RedisCache keeps at most 16 slot commands in flight; the keys must span more slots than that.
    private const int _MaxInFlightSlotCommands = 16;
    private const int _KeyCount = 200;
    private static readonly TimeSpan _Expiration = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task should_flush_native_values_across_slots_without_changing_their_redis_keys()
    {
        await fixture.FlushAllAsync(AbortToken);
        var clock = new FakeTimeProvider();
        using var origin = _CreateCache(clock);
        using var peer = _CreateCache(clock);
        const string counterKey = "native:}counter";
        const string setKey = "native:{set}:members";
        var database = fixture.ConnectionMultiplexer.GetDatabase();
        await origin.IncrementAsync(counterKey, 5L, expiration: null, AbortToken);
        await origin.SetAddAsync(setKey, new[] { "old" }, expiration: null, AbortToken);

        await origin.FlushAsync(AbortToken);

        (await database.StringGetAsync(counterKey)).ToString().Should().Be("5");
        (await database.SortedSetLengthAsync(setKey)).Should().Be(1);
        (await peer.GetSetAsync<string>(setKey, cancellationToken: AbortToken)).HasValue.Should().BeFalse();
        (await peer.SetRemoveAsync(setKey, new[] { "old" }, expiration: null, AbortToken)).Should().Be(0);
        (await peer.SetIfHigherAsync(counterKey, 1L, expiration: null, AbortToken)).Should().Be(1);
        (await peer.SetAddAsync(setKey, new[] { "new" }, expiration: null, AbortToken)).Should().Be(1);
        (await origin.GetSetAsync<string>(setKey, cancellationToken: AbortToken)).Value.Should().Equal("new");

        // A second flush has a distinct generation even when the clock reads the same timestamp.
        await origin.FlushAsync(AbortToken);

        (await peer.SetIfLowerAsync(counterKey, 2L, expiration: null, AbortToken)).Should().Be(2);
        (await peer.GetSetAsync<string>(setKey, cancellationToken: AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_flush_native_counters_on_each_same_timestamp_hybrid_flush()
    {
        await fixture.FlushAllAsync(AbortToken);
        // The native generation can advance past the last timestamp representable in a Redis frame.
        var clock = new FakeTimeProvider(DateTimeOffset.MaxValue);
        using var redis = _CreateCache(clock);
        using var local = new InMemoryCache(clock, new InMemoryCacheOptions());
        var backplane = Substitute.For<IHybridCacheBackplane>();
        await using var hybrid = new HybridCache(
            local,
            redis,
            backplane,
            new HybridCacheOptions(),
            timeProvider: clock
        );
        const string key = "hybrid:{native}:counter";

        await hybrid.FlushAsync(AbortToken);
        (await redis.IncrementAsync(key, 5L, expiration: null, AbortToken)).Should().Be(5);
        await hybrid.FlushAsync(AbortToken);

        (await redis.GetAsync<long>(key, AbortToken)).HasValue.Should().BeFalse();
        (await redis.IncrementAsync(key, 2L, expiration: null, AbortToken)).Should().Be(2);
        (await redis.GetAsync<long>(key, AbortToken)).Value.Should().Be(2);
    }

    [Fact]
    public async Task should_apply_each_flush_sequence_once_and_skip_older_timestamps()
    {
        await fixture.FlushAllAsync(AbortToken);
        var clock = new FakeTimeProvider();
        var timestamp = clock.GetUtcNow();
        using var redis = _CreateCache(clock);
        const string key = "hybrid:{dedup}:counter";
        await redis.WriteSequencedRemoveMarkerAsync(timestamp, "first", 1, AbortToken);
        await redis.WriteSequencedRemoveMarkerAsync(timestamp, "first", 2, AbortToken);
        await redis.IncrementAsync(key, 5L, expiration: null, AbortToken);

        await redis.WriteSequencedRemoveMarkerAsync(timestamp, "first", 2, AbortToken);
        await redis.WriteSequencedRemoveMarkerAsync(timestamp, "first", 1, AbortToken);
        (await redis.GetAsync<long>(key, AbortToken)).Value.Should().Be(5);

        await redis.WriteSequencedRemoveMarkerAsync(timestamp, "second", 1, AbortToken);
        (await redis.GetAsync<long>(key, AbortToken)).HasValue.Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(1));
        await redis.WriteSequencedRemoveMarkerAsync(clock.GetUtcNow(), "first", 3, AbortToken);
        await redis.IncrementAsync(key, 2L, expiration: null, AbortToken);
        await redis.WriteSequencedRemoveMarkerAsync(timestamp, "first", 4, AbortToken);
        (await redis.GetAsync<long>(key, AbortToken)).Value.Should().Be(2);
    }

    [Fact]
    public async Task should_remove_native_generation_when_replacing_counters_with_frames()
    {
        await fixture.FlushAllAsync(AbortToken);
        using var cache = _CreateCache();
        await cache.FlushAsync(AbortToken);
        const string scalarKey = "overwrite:native:scalar";
        const string batchKey = "overwrite:native:batch";
        const string insertKey = "overwrite:native:insert";
        await cache.IncrementAsync(scalarKey, 5L, expiration: null, AbortToken);
        await cache.IncrementAsync(batchKey, 5L, expiration: null, AbortToken);
        await cache.IncrementAsync(insertKey, 5L, expiration: null, AbortToken);

        await cache.UpsertAsync(scalarKey, "framed", _Expiration, AbortToken);
        await cache.UpsertAllAsync(
            new Dictionary<string, string>(StringComparer.Ordinal) { [batchKey] = "framed" },
            _Expiration,
            AbortToken
        );

        var database = fixture.ConnectionMultiplexer.GetDatabase();
        await database.KeyDeleteAsync(insertKey);
        (await cache.TryInsertAsync(insertKey, "framed", _Expiration, AbortToken)).Should().BeTrue();

        // A subsequent raw value that carries no generation must not inherit the overwritten counter's stamp.
        await database.StringSetAsync(scalarKey, 7L);
        await database.StringSetAsync(batchKey, 7L);
        await database.StringSetAsync(insertKey, 7L);

        var values = await cache.GetAllAsync<long>([scalarKey, batchKey, insertKey], AbortToken);
        values.Values.Should().OnlyContain(value => !value.HasValue);
    }

    [Fact]
    public async Task should_write_every_key_when_upserting_keys_that_span_many_slots()
    {
        // given
        await fixture.FlushAllAsync(AbortToken);
        using var cache = _CreateCache();
        var entries = _Entries("upsert:");

        // when
        var written = await cache.UpsertAllAsync(entries, _Expiration, AbortToken);

        // then
        written.Should().Be(_KeyCount);
        await _ShouldExistAsync(entries.Keys, exist: true);
    }

    [Fact]
    public async Task should_write_every_key_when_upserting_without_expiration_across_slots()
    {
        // given
        await fixture.FlushAllAsync(AbortToken);
        using var cache = _CreateCache();
        var entries = _Entries("upsert-forever:");

        // when
        var written = await cache.UpsertAllAsync(entries, expiration: null, AbortToken);

        // then
        written.Should().Be(_KeyCount);
        await _ShouldExistAsync(entries.Keys, exist: true);
    }

    [Fact]
    public async Task should_return_values_in_input_order_when_getting_keys_that_span_many_slots()
    {
        // given
        await fixture.FlushAllAsync(AbortToken);
        using var cache = _CreateCache();
        var entries = _Entries("get:");
        await cache.UpsertAllAsync(entries, _Expiration, AbortToken);

        // Interleave missing keys and shuffle, so a value placed by slot order instead of input index shows up as a
        // value under the wrong key or a hit on a missing key.
        var requested = entries
            .Keys.SelectMany((key, i) => new[] { key, $"get-missing:{i}" })
            .OrderBy(_ => Faker.Random.Int())
            .ToList();

        // when
        var result = await cache.GetAllAsync<string>(requested, AbortToken);
        var withExpiration = await cache.GetAllWithExpirationAsync<string>(requested, AbortToken);

        // then
        result.Keys.Should().Equal(requested);
        // GetAllWithExpirationAsync leaves out the keys it did not find.
        withExpiration.Keys.Should().Equal(requested.Where(entries.ContainsKey));

        foreach (var key in requested)
        {
            if (entries.TryGetValue(key, out var expected))
            {
                result[key].Value.Should().Be(expected);
                withExpiration[key].Value.Value.Should().Be(expected);
            }
            else
            {
                result[key].HasValue.Should().BeFalse();
            }
        }
    }

    [Fact]
    public async Task should_remove_every_key_when_removing_keys_that_span_many_slots()
    {
        // given
        await fixture.FlushAllAsync(AbortToken);
        using var cache = _CreateCache();
        var entries = _Entries("remove:");
        await cache.UpsertAllAsync(entries, _Expiration, AbortToken);

        // when
        var removed = await cache.RemoveAllAsync(entries.Keys, AbortToken);

        // then
        removed.Should().Be(_KeyCount);
        await _ShouldExistAsync(entries.Keys, exist: false);
    }

    [Fact]
    public async Task should_remove_only_prefixed_keys_on_every_primary_when_removing_by_prefix()
    {
        // given
        await fixture.FlushAllAsync(AbortToken);
        using var cache = _CreateCache();
        var doomed = _Entries("doomed:");
        var kept = _Entries("kept:");
        await cache.UpsertAllAsync(doomed, _Expiration, AbortToken);
        await cache.UpsertAllAsync(kept, _Expiration, AbortToken);

        // when
        var removed = await cache.RemoveByPrefixAsync("doomed:", AbortToken);

        // then
        removed.Should().Be(_KeyCount);
        await _ShouldExistAsync(doomed.Keys, exist: false);
        await _ShouldExistAsync(kept.Keys, exist: true);
    }

    [Fact]
    public async Task should_write_the_other_slots_when_one_slot_fails_during_upsert()
    {
        // given
        await fixture.FlushAllAsync(AbortToken);
        using var cache = _CreateCache();
        var entries = _Entries("upsert-fail:");
        var (failingKeys, otherKeys) = _SplitByFirstKeySlot(entries.Keys, out var failingSlot);

        await using (await fixture.UnassignSlotAsync(failingSlot, AbortToken))
        {
            // when
            var act = async () => await cache.UpsertAllAsync(entries, _Expiration, AbortToken);

            // then
            await act.Should().ThrowAsync<RedisServerException>().WithMessage("*CLUSTERDOWN*");
        }

        await _ShouldExistAsync(otherKeys, exist: true);
        await _ShouldExistAsync(failingKeys, exist: false);
    }

    [Fact]
    public async Task should_remove_the_other_slots_and_count_only_them_when_one_slot_fails_during_remove_all()
    {
        // given
        await fixture.FlushAllAsync(AbortToken);
        using var cache = _CreateCache();
        var entries = _Entries("remove-fail:");
        await cache.UpsertAllAsync(entries, _Expiration, AbortToken);
        var (failingKeys, otherKeys) = _SplitByFirstKeySlot(entries.Keys, out var failingSlot);

        // when
        int removed;

        await using (await fixture.UnassignSlotAsync(failingSlot, AbortToken))
        {
            removed = await cache.RemoveAllAsync(entries.Keys, AbortToken);
        }

        // then
        removed.Should().Be(otherKeys.Count);
        await _ShouldExistAsync(otherKeys, exist: false);
        await _ShouldExistAsync(failingKeys, exist: true);
    }

    [Fact]
    public async Task should_surface_the_failure_and_keep_serving_the_other_slots_when_one_slot_fails_during_get_all()
    {
        // given
        await fixture.FlushAllAsync(AbortToken);
        using var cache = _CreateCache();
        var entries = _Entries("get-fail:");
        await cache.UpsertAllAsync(entries, _Expiration, AbortToken);
        var (_, otherKeys) = _SplitByFirstKeySlot(entries.Keys, out var failingSlot);

        await using (await fixture.UnassignSlotAsync(failingSlot, AbortToken))
        {
            // when
            var act = async () => await cache.GetAllAsync<string>(entries.Keys, AbortToken);

            // then
            await act.Should().ThrowAsync<RedisServerException>().WithMessage("*CLUSTERDOWN*");

            var others = await cache.GetAllAsync<string>(otherKeys, AbortToken);
            others.Keys.Should().Equal(otherKeys);
            others.Should().AllSatisfy(pair => pair.Value.Value.Should().Be(entries[pair.Key]));
        }
    }

    [Fact]
    public async Task should_remove_the_other_slots_when_one_slot_fails_during_remove_by_prefix()
    {
        // given
        await fixture.FlushAllAsync(AbortToken);
        using var cache = _CreateCache();
        var entries = _Entries("prefix-fail:");
        await cache.UpsertAllAsync(entries, _Expiration, AbortToken);
        var (failingKeys, otherKeys) = _SplitByFirstKeySlot(entries.Keys, out var failingSlot);

        await using (await fixture.UnassignSlotAsync(failingSlot, AbortToken))
        {
            // when
            var act = async () => await cache.RemoveByPrefixAsync("prefix-fail:", AbortToken);

            // then
            await act.Should().ThrowAsync<RedisServerException>().WithMessage("*CLUSTERDOWN*");
        }

        await _ShouldExistAsync(otherKeys, exist: false);
        await _ShouldExistAsync(failingKeys, exist: true);
    }

    [Fact]
    public async Task should_remove_later_batches_on_the_failing_primary_when_one_slot_fails_during_remove_by_prefix()
    {
        // given: more matching keys per primary than one 250-key deletion batch, and a large share of the failing
        // primary's keys in the failing slot (a hash tag pins them there), so a failing batch on that primary comes
        // before another of its batches. Stopping at the failing batch would leave that later batch's keys behind.
        await fixture.FlushAllAsync(AbortToken);
        using var cache = _CreateCache();
        var pinned = Enumerable
            .Range(0, 300)
            .ToDictionary(i => $"sweep:{{pinned}}:{i}", i => $"value-{i}", StringComparer.Ordinal);
        var spread = Enumerable
            .Range(0, 900)
            .ToDictionary(i => $"sweep:{i}", i => $"value-{i}", StringComparer.Ordinal);
        await cache.UpsertAllAsync(pinned, _Expiration, AbortToken);
        await cache.UpsertAllAsync(spread, _Expiration, AbortToken);
        var failingSlot = fixture.ConnectionMultiplexer.HashSlot("{pinned}");
        var unpinnedInFailingSlot = spread.Keys.Where(key =>
            fixture.ConnectionMultiplexer.HashSlot(key) == failingSlot
        );

        await using (await fixture.UnassignSlotAsync(failingSlot, AbortToken))
        {
            // when
            var act = async () => await cache.RemoveByPrefixAsync("sweep:", AbortToken);

            // then
            await act.Should().ThrowAsync<RedisServerException>().WithMessage("*CLUSTERDOWN*");
        }

        await _ShouldExistAsync(spread.Keys.Except(unpinnedInFailingSlot, StringComparer.Ordinal), exist: false);
        await _ShouldExistAsync(pinned.Keys, exist: true);
    }

    private RedisCache _CreateCache(TimeProvider? timeProvider = null)
    {
        return new RedisCache(
            new SystemJsonSerializer(),
            timeProvider ?? TimeProvider.System,
            new RedisCacheOptions { ConnectionMultiplexer = fixture.ConnectionMultiplexer },
            fixture.ScriptsLoader,
            LoggerFactory.CreateLogger<RedisCache>()
        );
    }

    private Dictionary<string, string> _Entries(string prefix)
    {
        var entries = Enumerable
            .Range(0, _KeyCount)
            .ToDictionary(i => $"{prefix}{i}", i => $"value-{i}", StringComparer.Ordinal);

        var slots = entries.Keys.Select(key => fixture.ConnectionMultiplexer.HashSlot(key)).Distinct().Count();
        slots
            .Should()
            .BeGreaterThan(_MaxInFlightSlotCommands, "the keys must need more than one wave of slot commands");

        return entries;
    }

    private (List<string> FailingKeys, List<string> OtherKeys) _SplitByFirstKeySlot(
        IEnumerable<string> keys,
        out int failingSlot
    )
    {
        var multiplexer = fixture.ConnectionMultiplexer;
        var all = keys.ToList();
        var slot = multiplexer.HashSlot(all[0]);
        failingSlot = slot;

        var failing = all.Where(key => multiplexer.HashSlot(key) == slot).ToList();
        var others = all.Where(key => multiplexer.HashSlot(key) != slot).ToList();

        return (failing, others);
    }

    private async Task _ShouldExistAsync(IEnumerable<string> keys, bool exist)
    {
        var database = fixture.ConnectionMultiplexer.GetDatabase();

        foreach (var key in keys)
        {
            (await database.KeyExistsAsync(key))
                .Should()
                .Be(exist, "key {0} should {1}", key, exist ? "exist" : "be gone");
        }
    }
}
