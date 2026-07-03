// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class SetIfHigherLowerTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    #region SetIfHigher Long

    [Fact]
    public async Task should_set_if_higher_long_new_key()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetIfHigherAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(100);
    }

    [Fact]
    public async Task should_set_if_higher_long_when_value_is_higher()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfHigherAsync(key, 50L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(50);
    }

    [Fact]
    public async Task should_not_set_if_higher_long_when_value_is_lower()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfHigherAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 50L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0);
    }

    #endregion

    #region SetIfHigher Double

    [Fact]
    public async Task should_set_if_higher_double_new_key()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetIfHigherAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(100.5);
    }

    [Fact]
    public async Task should_set_if_higher_double_when_value_is_higher()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfHigherAsync(key, 50.5, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(50);
    }

    [Fact]
    public async Task should_not_set_if_higher_double_when_value_is_lower()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfHigherAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 50.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public async Task should_return_fractional_difference_for_set_if_higher_double()
    {
        // given — a non-integer difference exercises the Lua tostring(d) return branch; the integer-valued cases
        // above all land on string.format('%d', d), so the fractional path was previously uncovered.
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfHigherAsync(key, 50.25, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 100.75, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(50.5); // 100.75 - 50.25, returned via tostring(d)
    }

    #endregion

    #region SetIfLower Long

    [Fact]
    public async Task should_set_if_lower_long_new_key()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetIfLowerAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(100);
    }

    [Fact]
    public async Task should_set_if_lower_long_when_value_is_lower()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfLowerAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 50L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(50);
    }

    [Fact]
    public async Task should_not_set_if_lower_long_when_value_is_higher()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfLowerAsync(key, 50L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0);
    }

    #endregion

    #region SetIfLower Double

    [Fact]
    public async Task should_set_if_lower_double_new_key()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var result = await cache.SetIfLowerAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(100.5);
    }

    [Fact]
    public async Task should_set_if_lower_double_when_value_is_lower()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfLowerAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 50.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(50);
    }

    [Fact]
    public async Task should_not_set_if_lower_double_when_value_is_higher()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfLowerAsync(key, 50.5, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public async Task should_return_fractional_difference_for_set_if_lower_double()
    {
        // given — a non-integer difference exercises the Lua tostring(d) return branch (the integer-valued cases
        // above all land on string.format('%d', d)).
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfLowerAsync(key, 100.75, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 50.25, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(50.5); // 100.75 - 50.25, returned via tostring(d)
    }

    #endregion

    #region Mixed numeric types (#590)

    [Fact]
    public async Task should_truncate_when_set_if_higher_long_reads_a_fractional_stored_value()
    {
        // given — a fractional value written via the double overload, then a long SetIfHigher on the same key. The
        // difference (100 - 50.5 = 49.5) comes back via Lua tostring(d); the long overload must coerce it (truncate
        // toward zero) rather than throwing FormatException on long.Parse("49.5"). (#590)
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfHigherAsync(key, 50.5, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(49); // (long)(100 - 50.5), truncated toward zero
    }

    [Fact]
    public async Task should_truncate_when_set_if_lower_long_reads_a_fractional_stored_value()
    {
        // given — mirror of the SetIfHigher case: a fractional store then a long SetIfLower; the difference
        // (100.5 - 50 = 50.5) returns via tostring(d) and must truncate instead of throwing FormatException. (#590)
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfLowerAsync(key, 100.5, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 50L, TimeSpan.FromMinutes(5), AbortToken);

        // then
        result.Should().Be(50); // (long)(100.5 - 50), truncated toward zero
    }

    #endregion

    #region Zero Expiration

    [Fact]
    public async Task should_return_zero_when_set_if_higher_long_with_zero_expiration()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfHigherAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfHigherAsync(key, 150L, TimeSpan.Zero, AbortToken);

        // then
        result.Should().Be(0);
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_zero_when_set_if_lower_long_with_zero_expiration()
    {
        // given
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.SetIfLowerAsync(key, 100L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.SetIfLowerAsync(key, 50L, TimeSpan.Zero, AbortToken);

        // then
        result.Should().Be(0);
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeFalse();
    }

    #endregion
}
