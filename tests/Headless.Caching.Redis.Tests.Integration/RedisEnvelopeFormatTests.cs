// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using Headless.Caching;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisEnvelopeFormatTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    private const int _HeaderLength = 27;
    private const byte _Magic = 0xFF;
    private const byte _Version = 0x03;
    private const byte _NullFlag = 1 << 0;
    private const byte _HasPhysicalExpiresAtFlag = 1 << 2;
    private const byte _HasSlidingExpirationFlag = 1 << 3;

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
    public async Task should_reject_factory_store_write_when_concurrency_stamp_changed()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var store = (IFactoryCacheStore)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var now = DateTime.UtcNow;
        var originalEntry = new CacheStoreEntryWrite<string>
        {
            Value = "original",
            IsNull = false,
            LogicalExpiresAt = now.AddMinutes(5),
            PhysicalExpiresAt = now.AddMinutes(5),
        };
        await store.SetEntryAsync(key, in originalEntry, AbortToken);
        var snapshot = await store.TryGetEntryAsync<string>(key, AbortToken);

        var concurrentEntry = originalEntry with { Value = "concurrent" };
        await store.SetEntryAsync(key, in concurrentEntry, AbortToken);

        var staleWrite = originalEntry with { Value = "late", ExpectedConcurrencyStamp = snapshot.ConcurrencyStamp };

        var committed = await store.SetEntryAsync(key, in staleWrite, AbortToken);

        committed.Should().BeFalse();
        var value = await cache.GetAsync<string>(key, AbortToken);
        value.Value.Should().Be("concurrent");
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
    public async Task should_map_failsafe_physical_expiration_to_redis_ttl()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(5),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromSeconds(30),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(1),
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        var ttl = await _Database().KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeCloseTo(options.FailSafeMaxDuration, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task should_store_sliding_expiration_flag_and_map_redis_ttl_to_logical_deadline()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(30),
            SlidingExpiration = TimeSpan.FromSeconds(5),
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        var stored = await _GetRawBytesAsync(key);
        var ttl = await _Database().KeyTimeToLiveAsync(key);
        (stored[2] & _HasSlidingExpirationFlag).Should().Be(_HasSlidingExpirationFlag);
        // Value follows the fixed v3 header (27) plus the 8-byte sliding section = offset 35.
        stored.AsSpan(35).ToArray().Should().Equal(Encoding.UTF8.GetBytes("value"));
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeCloseTo(options.SlidingExpiration.Value, TimeSpan.FromSeconds(2));
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
    public async Task should_store_upsert_all_members_as_framed_payloads()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var stringKey = Faker.Random.AlphaNumeric(10);
        var nullKey = Faker.Random.AlphaNumeric(10);
        var stringValue = Faker.Lorem.Word();
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [stringKey] = stringValue,
            [nullKey] = null,
        };

        await cache.UpsertAllAsync(values, TimeSpan.FromMinutes(5), AbortToken);

        var storedString = await _GetRawBytesAsync(stringKey);
        storedString[0].Should().Be(_Magic);
        storedString[1].Should().Be(_Version);
        storedString.AsSpan(_HeaderLength).ToArray().Should().Equal(Encoding.UTF8.GetBytes(stringValue));

        var storedNull = await _GetRawBytesAsync(nullKey);
        storedNull[0].Should().Be(_Magic);
        storedNull[1].Should().Be(_Version);
        (storedNull[2] & _NullFlag).Should().Be(_NullFlag);
    }

    [Fact]
    public async Task should_encode_logical_expiration_header_as_now_plus_duration()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var duration = TimeSpan.FromMinutes(5);
        var before = DateTimeOffset.UtcNow;

        await cache.UpsertAsync(key, "value", duration, AbortToken);

        var after = DateTimeOffset.UtcNow;
        var stored = await _GetRawBytesAsync(key);
        var logicalMs = BinaryPrimitives.ReadInt64LittleEndian(stored.AsSpan(3, sizeof(long)));
        var logical = DateTimeOffset.FromUnixTimeMilliseconds(logicalMs);

        logical.Should().BeOnOrAfter(before + duration - TimeSpan.FromSeconds(5));
        logical.Should().BeOnOrBefore(after + duration + TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task should_round_trip_entry_metadata_through_factory_store()
    {
        await FlushAsync();
        using var cache = CreateCache();
        var store = (IFactoryCacheStore)cache;
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Word();
        var now = DateTime.UtcNow;
        var entry = new CacheStoreEntryWrite<string>
        {
            Value = value,
            IsNull = false,
            LogicalExpiresAt = now.AddMinutes(5),
            PhysicalExpiresAt = now.AddMinutes(10),
            EagerRefreshAt = now.AddMinutes(4),
            ETag = "W/\"v42\"",
            LastModifiedAt = now.AddMinutes(-30),
            Tags = ["tenant:1", "products"],
        };

        await store.SetEntryAsync(key, in entry, AbortToken);

        var roundTripped = await store.TryGetEntryAsync<string>(key, AbortToken);
        roundTripped.Found.Should().BeTrue();
        roundTripped.Value.Should().Be(value);
        roundTripped.EagerRefreshAt.Should().BeCloseTo(entry.EagerRefreshAt!.Value, TimeSpan.FromMilliseconds(1));
        roundTripped.ETag.Should().Be(entry.ETag);
        roundTripped.LastModifiedAt.Should().BeCloseTo(entry.LastModifiedAt!.Value, TimeSpan.FromMilliseconds(1));
        roundTripped.Tags.Should().Equal("tenant:1", "products");
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
