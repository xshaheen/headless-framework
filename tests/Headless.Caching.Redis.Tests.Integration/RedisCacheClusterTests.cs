// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Serializer;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
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

    private RedisCache _CreateCache()
    {
        return new RedisCache(
            new SystemJsonSerializer(),
            TimeProvider.System,
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
