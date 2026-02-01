// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Tests;

public sealed class KeyPrefixTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_prefix_keys_with_configured_prefix()
    {
        // given
        await FlushAsync();
        const string prefix = "myapp:";
        var cache = CreateCache(prefix);
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();

        // when
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then - verify key is stored with prefix in Redis directly
        var db = Fixture.ConnectionMultiplexer.GetDatabase();
        var prefixedKey = prefix + key;
        var storedValue = await db.StringGetAsync(prefixedKey);
        storedValue.HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task should_isolate_caches_with_different_prefixes()
    {
        // given
        await FlushAsync();
        var cache1 = CreateCache("tenant1:");
        var cache2 = CreateCache("tenant2:");
        const string key = "shared-key";

        // when
        await cache1.UpsertAsync(key, "value-for-tenant1", TimeSpan.FromMinutes(5), AbortToken);
        await cache2.UpsertAsync(key, "value-for-tenant2", TimeSpan.FromMinutes(5), AbortToken);

        // then
        var value1 = await cache1.GetAsync<string>(key, AbortToken);
        var value2 = await cache2.GetAsync<string>(key, AbortToken);
        value1.Value.Should().Be("value-for-tenant1");
        value2.Value.Should().Be("value-for-tenant2");
    }

    [Fact]
    public async Task should_apply_prefix_to_all_operations()
    {
        // given
        await FlushAsync();
        const string prefix = "prefixed:";
        var cache = CreateCache(prefix);
        var key = Faker.Random.AlphaNumeric(10);

        // when
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);
        var exists = await cache.ExistsAsync(key, AbortToken);
        var expiration = await cache.GetExpirationAsync(key, AbortToken);
        var removed = await cache.RemoveAsync(key, AbortToken);

        // then
        exists.Should().BeTrue();
        expiration.Should().NotBeNull();
        removed.Should().BeTrue();
    }

    [Fact]
    public async Task should_work_without_prefix()
    {
        // given
        await FlushAsync();
        var cache = CreateCache(keyPrefix: "");
        var key = Faker.Random.AlphaNumeric(10);
        var value = Faker.Lorem.Sentence();

        // when
        await cache.UpsertAsync(key, value, TimeSpan.FromMinutes(5), AbortToken);

        // then
        var db = Fixture.ConnectionMultiplexer.GetDatabase();
        var storedValue = await db.StringGetAsync(key);
        storedValue.HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task should_apply_prefix_to_get_by_prefix()
    {
        // given
        await FlushAsync();
        const string appPrefix = "app:";
        var cache = CreateCache(appPrefix);
        await cache.UpsertAsync("users:1", "user1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("users:2", "user2", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("orders:1", "order1", TimeSpan.FromMinutes(5), AbortToken);

        // when - search for "users:" prefix (cache will add "app:" automatically)
        var result = await cache.GetByPrefixAsync<string>("users:", AbortToken);

        // then
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_apply_prefix_to_remove_by_prefix()
    {
        // given
        await FlushAsync();
        const string appPrefix = "app:";
        var cache = CreateCache(appPrefix);
        await cache.UpsertAsync("users:1", "user1", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("users:2", "user2", TimeSpan.FromMinutes(5), AbortToken);
        await cache.UpsertAsync("orders:1", "order1", TimeSpan.FromMinutes(5), AbortToken);

        // when
        var removed = await cache.RemoveByPrefixAsync("users:", AbortToken);

        // then
        removed.Should().Be(2);
        var ordersExists = await cache.ExistsAsync("orders:1", AbortToken);
        ordersExists.Should().BeTrue();
    }
}
