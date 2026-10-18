// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Caching;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Tests;

/// <summary>
/// Runs against its own Redis container, so the server script cache starts empty and no other test
/// can load a script between the host start and the assertions.
/// </summary>
public sealed class RedisCacheScriptLoadingTests : TestBase
{
    private readonly RedisContainer _container = new RedisBuilder(TestImages.Redis).Build();
    private ConnectionMultiplexer _multiplexer = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await _container.StartAsync(AbortToken);
        _multiplexer = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString() + ",allowAdmin=true");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.DisposeAsync();
        }

        await _container.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_start_without_loading_scripts_and_load_them_on_first_use_when_use_redis()
    {
        // given - a default Redis cache plus a named instance, each with its own scripts loader
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessCaching(setup =>
        {
            setup.UseRedis(options => options.ConnectionMultiplexer = _multiplexer);
            setup.AddNamed(
                "tenant",
                instance =>
                    instance.UseRedis(options =>
                    {
                        options.ConnectionMultiplexer = _multiplexer;
                        options.KeyPrefix = "script-loading-tenant:";
                    })
            );
        });

        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);

        // then - the host start made no SCRIPT LOAD
        (await _CachedScriptCountAsync())
            .Should()
            .Be(0);

        // when - the first script-backed operation on each instance
        var defaultCache = host.Services.GetRequiredService<ICache>();
        var namedCache = host.Services.GetRequiredKeyedService<ICache>("tenant");
        var defaultFirst = await defaultCache.IncrementAsync("counter", 2L, TimeSpan.FromMinutes(5), AbortToken);
        var defaultSecond = await defaultCache.IncrementAsync("counter", 3L, TimeSpan.FromMinutes(5), AbortToken);
        var namedFirst = await namedCache.IncrementAsync("counter", 7L, TimeSpan.FromMinutes(5), AbortToken);

        // then - each operation loaded its script on demand and returned the right value
        defaultFirst.Should().Be(2);
        defaultSecond.Should().Be(5);
        namedFirst.Should().Be(7);
        (await _CachedScriptCountAsync()).Should().BePositive();

        await host.StopAsync(AbortToken);
    }

    private async Task<long> _CachedScriptCountAsync()
    {
        var server = _multiplexer.GetServer(_multiplexer.GetEndPoints().Single());
        var info = await server.InfoAsync("memory");
        var count = info.SelectMany(group => group)
            .Single(pair => string.Equals(pair.Key, "number_of_cached_scripts", StringComparison.Ordinal))
            .Value;

        return long.Parse(count, CultureInfo.InvariantCulture);
    }
}
