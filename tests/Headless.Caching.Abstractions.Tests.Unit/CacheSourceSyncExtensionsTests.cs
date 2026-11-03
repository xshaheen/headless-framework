// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;

namespace Tests;

public sealed class CacheSourceSyncExtensionsTests : TestBase
{
    private static readonly TimeSpan _Expiration = TimeSpan.FromMinutes(5);

    private readonly InMemoryCache _memoryCache = new(TimeProvider.System, new InMemoryCacheOptions());
    private readonly Cache<Item> _cache;

    public CacheSourceSyncExtensionsTests()
    {
        _cache = new Cache<Item>(_memoryCache);
    }

    [Fact]
    public async Task should_insert_the_source_value_when_the_entry_is_absent()
    {
        // when
        await _cache.SyncWithSourceAsync(["a"], _Source(new() { ["a"] = "fresh" }), _Expiration, AbortToken);

        // then
        (await _cache.GetAsync("a", AbortToken))
            .Value!.Value.Should()
            .Be("fresh");
    }

    [Fact]
    public async Task should_replace_the_entry_with_the_source_value_when_present()
    {
        // given
        await _cache.UpsertAsync("a", new Item("stale"), _Expiration, AbortToken);

        // when
        await _cache.SyncWithSourceAsync(["a"], _Source(new() { ["a"] = "fresh" }), _Expiration, AbortToken);

        // then
        (await _cache.GetAsync("a", AbortToken))
            .Value!.Value.Should()
            .Be("fresh");
    }

    [Fact]
    public async Task should_read_again_and_land_the_newer_source_value_when_another_write_lands_mid_sync()
    {
        // given a source whose first read races a newer write: the write commits and reaches the cache after this
        // sync read the entry, and the read still returns the value from before it
        await _cache.UpsertAsync("a", new Item("old"), _Expiration, AbortToken);
        var source = "old";
        var reads = 0;

        async ValueTask<IReadOnlyDictionary<string, Item>> readSource(
            IReadOnlyCollection<string> keys,
            CancellationToken token
        )
        {
            var read = source;

            if (++reads == 1)
            {
                source = "newer";
                await _cache.UpsertAsync("a", new Item("newer"), _Expiration, token);
            }

            return keys.ToDictionary(key => key, _ => new Item(read), StringComparer.Ordinal);
        }

        // when
        await _cache.SyncWithSourceAsync(["a"], readSource, _Expiration, AbortToken);

        // then the stale read lost its swap, and the retry stored the newer value
        reads.Should().Be(2);
        (await _cache.GetAsync("a", AbortToken)).Value!.Value.Should().Be("newer");
    }

    [Fact]
    public async Task should_not_swap_over_a_newer_write_of_an_equal_value()
    {
        // given an entry this sync reads, then a newer write of the same value lands while the source read is stale
        await _cache.UpsertAsync("a", new Item("same"), _Expiration, AbortToken);
        var reads = 0;

        async ValueTask<IReadOnlyDictionary<string, Item>> readSource(
            IReadOnlyCollection<string> keys,
            CancellationToken token
        )
        {
            if (++reads == 1)
            {
                await _cache.UpsertAsync("a", new Item("same"), _Expiration, token);

                return new Dictionary<string, Item>(StringComparer.Ordinal) { ["a"] = new("stale") };
            }

            return new Dictionary<string, Item>(StringComparer.Ordinal) { ["a"] = new("same") };
        }

        // when
        await _cache.SyncWithSourceAsync(["a"], readSource, _Expiration, AbortToken);

        // then the stamp told the two equal values apart, so the stale read never landed
        reads.Should().Be(2);
        (await _cache.GetAsync("a", AbortToken)).Value!.Value.Should().Be("same");
    }

    [Fact]
    public async Task should_remove_a_key_the_source_omits()
    {
        // given
        await _cache.UpsertAsync("a", new Item("stale"), _Expiration, AbortToken);

        // when
        await _cache.SyncWithSourceAsync(["a"], _Source([]), _Expiration, AbortToken);

        // then
        (await _cache.GetAsync("a", AbortToken))
            .HasValue.Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_remove_the_entry_when_every_round_is_contended()
    {
        // given a source whose every read races another write to the entry
        var reads = 0;

        async ValueTask<IReadOnlyDictionary<string, Item>> readSource(
            IReadOnlyCollection<string> keys,
            CancellationToken token
        )
        {
            reads++;
            await _cache.UpsertAsync("a", new Item("racing"), _Expiration, token);

            return keys.ToDictionary(key => key, _ => new Item("stale"), StringComparer.Ordinal);
        }

        // when
        await _cache.SyncWithSourceAsync(["a"], readSource, _Expiration, AbortToken);

        // then it gives up after four rounds and leaves the next read to load from the source
        reads.Should().Be(4);
        (await _cache.GetAsync("a", AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_read_only_the_keys_still_pending_on_a_retry()
    {
        // given two keys, one of which a racing write contends on the first round
        var requested = new List<string[]>();

        async ValueTask<IReadOnlyDictionary<string, Item>> readSource(
            IReadOnlyCollection<string> keys,
            CancellationToken token
        )
        {
            requested.Add([.. keys]);

            if (requested.Count == 1)
            {
                await _cache.UpsertAsync("b", new Item("racing"), _Expiration, token);
            }

            return keys.ToDictionary(key => key, key => new Item($"fresh-{key}"), StringComparer.Ordinal);
        }

        // when
        await _cache.SyncWithSourceAsync(["a", "b", "a"], readSource, _Expiration, AbortToken);

        // then
        requested.Should().HaveCount(2);
        requested[0].Should().BeEquivalentTo(["a", "b"]);
        requested[1].Should().Equal("b");
        (await _cache.GetAsync("a", AbortToken)).Value!.Value.Should().Be("fresh-a");
        (await _cache.GetAsync("b", AbortToken)).Value!.Value.Should().Be("fresh-b");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _memoryCache.Dispose();
        await base.DisposeAsyncCore();
    }

    private static Func<
        IReadOnlyCollection<string>,
        CancellationToken,
        ValueTask<IReadOnlyDictionary<string, Item>>
    > _Source(Dictionary<string, string> values)
    {
        return (keys, _) =>
        {
            var items = new Dictionary<string, Item>(StringComparer.Ordinal);

            foreach (var key in keys)
            {
                if (values.TryGetValue(key, out var value))
                {
                    items[key] = new Item(value);
                }
            }

            return ValueTask.FromResult<IReadOnlyDictionary<string, Item>>(items);
        };
    }

    private sealed record Item(string Value)
    {
        public Guid Stamp { get; init; } = Guid.NewGuid();
    }
}
