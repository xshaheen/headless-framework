// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisEnvelopeFormatTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    private const int _HeaderLength = 19;
    private const byte _Magic = 0xFF;
    private const byte _Version = 0x01;
    private const byte _NullFlag = 1 << 0;
    private const byte _HasPhysicalExpiresAtFlag = 1 << 2;

    [Fact]
    public async Task should_store_scalar_value_as_framed_payload_with_magic_prefix()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Word();

        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        var stored = await _GetRawBytesAsync(key);
        stored[0].Should().Be(_Magic);
        stored[1].Should().Be(_Version);
        stored.AsSpan(_HeaderLength).ToArray().Should().Equal(Encoding.UTF8.GetBytes(value));
    }

    [Fact]
    public async Task should_map_physical_expiration_to_redis_ttl()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var duration = TimeSpan.FromMinutes(5);

        await cache.UpsertAsync(key, "value", duration, AbortToken);

        var ttl = await _Database().KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeCloseTo(duration, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task should_store_null_as_frame_flag_with_empty_value_segment()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        await cache.UpsertAsync<string?>(key, null, TimeSpan.FromMinutes(5), AbortToken);

        var stored = await _GetRawBytesAsync(key);
        (stored[2] & _NullFlag).Should().Be(_NullFlag);
        stored.Length.Should().Be(_HeaderLength);

        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.IsNull.Should().BeTrue();
    }

    [Fact]
    public async Task should_round_trip_literal_null_sentinel_string_as_value()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        await cache.UpsertAsync(key, "@@NULL", TimeSpan.FromMinutes(5), AbortToken);

        var cached = await cache.GetAsync<string>(key, AbortToken);
        cached.Value.Should().Be("@@NULL");
    }

    [Fact]
    public async Task should_keep_legacy_raw_null_sentinel_readable_as_null()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await _Database().StringSetAsync(key, "@@NULL");

        var cached = await cache.GetAsync<string>(key, AbortToken);

        cached.HasValue.Should().BeTrue();
        cached.IsNull.Should().BeTrue();
    }

    [Fact]
    public async Task should_store_no_expiration_frame_without_physical_expiration_flag_or_ttl()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        await cache.UpsertAsync(key, "value", expiration: null, AbortToken);

        var stored = await _GetRawBytesAsync(key);
        (stored[2] & _HasPhysicalExpiresAtFlag).Should().Be(0);
        var ttl = await _Database().KeyTimeToLiveAsync(key);
        ttl.Should().BeNull();
    }

    [Fact]
    public async Task should_keep_increment_counter_raw_and_unframed()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        await cache.IncrementAsync(key, 5L, TimeSpan.FromMinutes(5), AbortToken);

        var stored = await _GetRawBytesAsync(key);
        stored.Should().Equal(Encoding.UTF8.GetBytes("5"));
    }

    private IDatabase _Database() => Fixture.ConnectionMultiplexer.GetDatabase();

    private async Task<byte[]> _GetRawBytesAsync(string key)
    {
        var value = await _Database().StringGetAsync(key);
        value.HasValue.Should().BeTrue();
        return (byte[])value!;
    }
}
