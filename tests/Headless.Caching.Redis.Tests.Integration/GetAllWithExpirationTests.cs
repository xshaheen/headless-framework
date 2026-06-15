// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// Integration tests for <c>GetAllWithExpirationAsync</c> covering the persistent-key (no TTL) path
/// that was previously mis-classified as a miss.
/// </summary>
public sealed class GetAllWithExpirationTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_return_hit_with_null_expiration_for_persistent_key()
    {
        // given — write a key with NO expiration (persistent, no Redis TTL)
        await FlushAsync();
        var key = Faker.Random.AlphaNumeric(10);
        using var cache = CreateCache();
        const string expectedValue = "persistent-value";
        await cache.UpsertAsync(key, expectedValue, expiration: null, AbortToken);

        // when
        var result = await cache.GetAllWithExpirationAsync<string>([key], AbortToken);

        // then — the key must be a hit, not dropped
        result.Should().ContainKey(key);
        var entry = result[key];
        entry.Value.HasValue.Should().BeTrue("persistent key is a cache hit");
        entry.Value.Value.Should().Be(expectedValue);
        entry.Expiration.Should().BeNull("persistent key has no expiry");
    }

    [Fact]
    public async Task should_return_hit_with_null_expiration_for_persistent_legacy_key_alongside_expiring_keys()
    {
        // given — mix of a persistent key and a normal expiring key
        await FlushAsync();
        var persistentKey = Faker.Random.AlphaNumeric(10);
        var expiringKey = Faker.Random.AlphaNumeric(10);
        using var cache = CreateCache();

        await cache.UpsertAsync(persistentKey, "persist", expiration: null, AbortToken);
        await cache.UpsertAsync(expiringKey, "expires", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetAllWithExpirationAsync<string>([persistentKey, expiringKey], AbortToken);

        // then — both keys are hits
        result.Should().HaveCount(2);

        result.Should().ContainKey(persistentKey);
        result[persistentKey].Value.HasValue.Should().BeTrue();
        result[persistentKey].Value.Value.Should().Be("persist");
        result[persistentKey].Expiration.Should().BeNull("persistent key carries no TTL");

        result.Should().ContainKey(expiringKey);
        result[expiringKey].Value.HasValue.Should().BeTrue();
        result[expiringKey].Expiration.Should().NotBeNull("expiring key has a remaining TTL");
        result[expiringKey].Expiration!.Value.Should().BeGreaterThan(TimeSpan.Zero);
    }
}
