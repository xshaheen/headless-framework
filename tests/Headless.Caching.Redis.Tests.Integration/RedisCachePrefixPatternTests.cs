// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>A prefix is matched literally, so glob metacharacters in it never reach other keys.</summary>
public sealed class RedisCachePrefixPatternTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    public static TheoryData<string> GlobPrefixes => ["a*:", "a?c:", "a[bx]c:", @"a\bc:"];

    [Theory]
    [MemberData(nameof(GlobPrefixes))]
    public async Task should_remove_only_keys_under_the_literal_prefix(string prefix)
    {
        await FlushAsync();
        using var cache = CreateCache($"{Faker.Random.AlphaNumeric(8)}:");
        await cache.UpsertAsync($"{prefix}own", "own", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("abc:other", "other", TimeSpan.FromMinutes(5), AbortToken);

        var removed = await cache.RemoveByPrefixAsync(prefix, AbortToken);

        removed.Should().Be(1);
        (await cache.GetAsync<string>($"{prefix}own", AbortToken)).HasValue.Should().BeFalse();
        (await cache.GetAsync<string>("abc:other", AbortToken)).Value.Should().Be("other");
    }

    [Theory]
    [MemberData(nameof(GlobPrefixes))]
    public async Task should_count_and_list_only_keys_under_the_literal_prefix(string prefix)
    {
        await FlushAsync();
        using var cache = CreateCache($"{Faker.Random.AlphaNumeric(8)}:");
        await cache.UpsertAsync($"{prefix}own", "own", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("abc:other", "other", TimeSpan.FromMinutes(5), AbortToken);

        (await cache.GetCountAsync(prefix, AbortToken)).Should().Be(1);
        (await cache.GetAllKeysByPrefixAsync(prefix, AbortToken)).Should().Equal($"{prefix}own");
    }

    [Fact]
    public async Task should_match_a_configured_key_prefix_holding_glob_characters_literally()
    {
        await FlushAsync();
        using var globbed = CreateCache("t*:");
        using var other = CreateCache("tx:");
        await globbed.UpsertAsync("k", "own", TimeSpan.FromMinutes(5), AbortToken);
        await other.UpsertAsync("k", "other", TimeSpan.FromMinutes(5), AbortToken);

        (await globbed.GetCountAsync(cancellationToken: AbortToken)).Should().Be(1);
        await globbed.RemoveByPrefixAsync("k", AbortToken);

        (await other.GetAsync<string>("k", AbortToken)).Value.Should().Be("other");
    }
}
