// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class TryReplaceTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_replace_when_key_exists()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var originalValue = Faker.Lorem.Sentence();
        var newValue = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, originalValue, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceAsync(key, newValue, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be(newValue);
    }

    [Fact]
    public async Task should_not_replace_when_key_not_exists()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();

        // when
        var result = await cache.TryReplaceAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeFalse();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_replace_if_equal_when_expected_matches()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var originalValue = Faker.Lorem.Sentence();
        var newValue = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, originalValue, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceIfEqualAsync(
            key,
            originalValue,
            newValue,
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be(newValue);
    }

    [Fact]
    public async Task should_not_replace_if_equal_when_expected_not_matches()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var originalValue = Faker.Lorem.Sentence();
        var wrongExpected = Faker.Lorem.Sentence();
        var newValue = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, originalValue, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceIfEqualAsync(
            key,
            wrongExpected,
            newValue,
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.Should().BeFalse();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be(originalValue);
    }

    [Fact]
    public async Task should_not_replace_if_equal_when_key_not_exists()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var expected = Faker.Lorem.Sentence();
        var newValue = Faker.Lorem.Sentence();

        // when
        var result = await cache.TryReplaceIfEqualAsync(key, expected, newValue, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_replace_if_equal_when_expected_is_null_and_stored_frame_is_null()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync<string?>(key, null, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceIfEqualAsync<string?>(
            key,
            null,
            "value",
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be("value");
    }

    [Fact]
    public async Task should_replace_if_equal_on_raw_unframed_counter_value()
    {
        // given — counters are stored raw/unframed, exercising the Lua else branch
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.IncrementAsync(key, 5L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceIfEqualAsync(key, 5L, 10L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<long>(key, AbortToken);
        cached.Value.Should().Be(10L);
    }

    [Fact]
    public async Task should_replace_if_equal_comparing_value_segment_only_when_headers_differ()
    {
        // given — re-upserting the same value rewrites the physical timestamp in the frame header,
        // so a whole-blob compare would fail; only a value-segment comparison succeeds.
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();
        var newValue = Faker.Lorem.Sentence();
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // Re-upsert the identical value at a later instant so the header bytes (physical timestamp) drift
        await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceIfEqualAsync(key, value, newValue, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().BeTrue();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be(newValue);
    }

    [Fact]
    public async Task should_not_replace_if_equal_when_expected_is_null_and_stored_frame_is_not_null()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.TryReplaceIfEqualAsync<string?>(
            key,
            null,
            "new-value",
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // then
        result.Should().BeFalse();
        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be("value");
    }
}
