// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A logical clear, tag removal, or expiry leaves the entry resident while every read reports it absent; the
/// conditional writes must agree with those reads instead of with the resident entry, as the Redis provider does.
/// </summary>
public sealed class InMemoryCacheConditionalWriteAfterLogicalRemovalTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_insert_when_the_entry_was_cleared()
    {
        var (cache, key) = await _CreateWithStaleEntryAsync(c => c.ClearAsync(AbortToken).AsTask());
        using var _ = cache;

        var inserted = await cache.TryInsertAsync(key, "fresh", TimeSpan.FromMinutes(5), AbortToken);

        inserted.Should().BeTrue("the cleared entry is absent to every read");
        (await cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("fresh");
        (await cache.TryInsertAsync(key, "second", TimeSpan.FromMinutes(5), AbortToken))
            .Should()
            .BeFalse("the inserted entry is live");
    }

    [Fact]
    public async Task should_insert_when_the_entry_was_tag_invalidated()
    {
        var (cache, key) = await _CreateWithStaleEntryAsync(c => c.RemoveByTagAsync("t", AbortToken).AsTask());
        using var _ = cache;

        (await cache.TryInsertAsync(key, "fresh", TimeSpan.FromMinutes(5), AbortToken)).Should().BeTrue();
        (await cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_insert_when_the_entry_is_logically_expired_inside_its_reserve()
    {
        using var cache = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var key = Faker.Random.AlphaNumeric(10);
        var failSafe = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(1),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(10),
        };
        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("old"), failSafe, AbortToken);
        (await cache.ExpireAsync(key, AbortToken)).Should().BeTrue();
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();

        (await cache.TryInsertAsync(key, "fresh", TimeSpan.FromMinutes(5), AbortToken)).Should().BeTrue();
        (await cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_not_replace_if_equal_when_the_entry_was_cleared()
    {
        var (cache, key) = await _CreateWithStaleEntryAsync(c => c.ClearAsync(AbortToken).AsTask());
        using var _ = cache;

        var replaced = await cache.TryReplaceIfEqualAsync(key, "old", "new", TimeSpan.FromMinutes(5), AbortToken);

        replaced.Should().BeFalse("no read sees the expected value any more");
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_remove_if_equal_when_the_entry_was_tag_invalidated()
    {
        var (cache, key) = await _CreateWithStaleEntryAsync(c => c.RemoveByTagAsync("t", AbortToken).AsTask());
        using var _ = cache;

        (await cache.RemoveIfEqualAsync(key, "old", AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_not_replace_when_the_entry_was_cleared()
    {
        var (cache, key) = await _CreateWithStaleEntryAsync(c => c.ClearAsync(AbortToken).AsTask());
        using var _ = cache;

        (await cache.TryReplaceAsync(key, "new", TimeSpan.FromMinutes(5), AbortToken)).Should().BeFalse();
        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_compare_and_swap_when_the_entry_is_live()
    {
        using var cache = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "old", TimeSpan.FromMinutes(5), AbortToken);

        (await cache.TryInsertAsync(key, "other", TimeSpan.FromMinutes(5), AbortToken)).Should().BeFalse();
        (await cache.TryReplaceIfEqualAsync(key, "old", "new", TimeSpan.FromMinutes(5), AbortToken)).Should().BeTrue();
        (await cache.TryReplaceAsync(key, "newer", TimeSpan.FromMinutes(5), AbortToken)).Should().BeTrue();
        (await cache.RemoveIfEqualAsync(key, "newer", AbortToken)).Should().BeTrue();
    }

    private async Task<(InMemoryCache Cache, string Key)> _CreateWithStaleEntryAsync(
        Func<InMemoryCache, Task> invalidate
    )
    {
        var cache = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertEntryAsync(
            key,
            "old",
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = ["t"] },
            AbortToken
        );

        // A marker invalidates only entries born strictly before it.
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        await invalidate(cache);

        (await cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse("the entry is logically removed");
        (await cache.GetCountAsync(cancellationToken: AbortToken)).Should().Be(1, "the entry is still resident");

        return (cache, key);
    }
}
