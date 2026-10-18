// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection(nameof(RedisCacheFixture))]
public sealed class RedisCacheSetupTests(RedisCacheFixture fixture) : TestBase
{
    [Fact]
    public async Task should_register_default_cache_role_key_and_generic_adapters_when_use_redis()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHeadlessCaching(setup =>
            setup.UseRedis(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer)
        );

        using var host = builder.Build();
        await host.StartAsync(AbortToken);

        // when
        var defaultCache = host.Services.GetRequiredService<ICache>();

        // then - the unkeyed default is the Redis cache, aliased under the remote role key
        defaultCache.Should().BeOfType<RedisCache>();
        host.Services.GetRequiredService<IRemoteCache>().Should().BeSameAs(defaultCache);
        host.Services.GetRequiredKeyedService<ICache>(CacheConstants.RemoteCacheProvider)
            .Should()
            .BeSameAs(defaultCache);

        // then - the generic adapter resolves over the default cache
        host.Services.GetRequiredService<ICache<RedisCacheSetupTests>>().Should().NotBeNull();

        await host.StopAsync(AbortToken);
    }
}
