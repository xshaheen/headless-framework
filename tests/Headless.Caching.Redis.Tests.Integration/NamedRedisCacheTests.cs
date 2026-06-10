// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>Tests for named Redis cache instances coexisting with the default registration.</summary>
[Collection(nameof(RedisCacheFixture))]
public sealed class NamedRedisCacheTests(RedisCacheFixture fixture) : TestBase
{
    [Fact]
    public async Task named_redis_cache_should_be_isolated_by_prefix_and_honor_default_entry_options()
    {
        // given - a default Redis cache plus a named instance with its own prefix and entry defaults
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddRedisCache(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer);

        builder.Services.AddRedisCache(
            "tenant",
            options =>
            {
                options.ConnectionMultiplexer = fixture.ConnectionMultiplexer;
                options.KeyPrefix = "named-tenant:";
                options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(7) };
            }
        );

        using var host = builder.Build();
        await host.StartAsync(AbortToken);

        var cacheProvider = host.Services.GetRequiredService<ICacheProvider>();
        var named = cacheProvider.GetCache("tenant");
        var defaultCache = host.Services.GetRequiredService<ICache>();
        named.Should().NotBeSameAs(defaultCache);

        // when - write through the named instance
        var key = Faker.Random.AlphaNumeric(12);
        await named.UpsertAsync(key, "named-value", TimeSpan.FromMinutes(5), AbortToken);

        // then - the default (unprefixed) cache does not see it, and the prefixed key exists in Redis
        (await defaultCache.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();
        (await named.GetAsync<string>(key, AbortToken)).Value.Should().Be("named-value");
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        (await db.KeyExistsAsync("named-tenant:" + key)).Should().BeTrue();

        // then - the no-options GetOrAddAsync extension applies the named instance's defaults
        var factoryKey = Faker.Random.AlphaNumeric(12);
        var result = await named.GetOrAddAsync<string>(
            factoryKey,
            _ => ValueTask.FromResult<string?>("factory-value"),
            AbortToken
        );
        result.Value.Should().Be("factory-value");

        var expiration = await named.GetExpirationAsync(factoryKey, AbortToken);
        expiration.Should().NotBeNull();
        expiration!.Value.Should().BeCloseTo(TimeSpan.FromMinutes(7), TimeSpan.FromSeconds(30));

        // then - the default cache has no DefaultEntryOptions, so the option-less overload throws
        var act = async () =>
            await defaultCache.GetOrAddAsync<string>(key, _ => ValueTask.FromResult<string?>("v"), AbortToken);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DefaultEntryOptions*");

        await host.StopAsync(AbortToken);
    }
}
