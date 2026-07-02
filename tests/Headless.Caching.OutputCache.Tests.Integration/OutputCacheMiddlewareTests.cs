// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Exercises the full ASP.NET output-cache pipeline against the Redis-backed Headless store: a tagged endpoint
/// served from cache on replay, and re-executed after the store's <c>EvictByTagAsync</c>.
/// </summary>
[Collection(nameof(OutputCacheRedisFixture))]
public sealed class OutputCacheMiddlewareTests(OutputCacheRedisFixture fixture) : TestBase
{
    [Fact]
    public async Task second_identical_request_is_served_from_the_redis_backed_store()
    {
        using var host = await _CreateHostAsync();
        using var client = host.GetTestClient();

        var first = await client.GetStringAsync("/counter", AbortToken);
        var second = await client.GetStringAsync("/counter", AbortToken);

        // the handler ran once; the second response is the replayed cached body
        first.Should().Be("1");
        second.Should().Be("1");
    }

    [Fact]
    public async Task request_re_executes_after_evict_by_tag()
    {
        using var host = await _CreateHostAsync();
        using var client = host.GetTestClient();

        var first = await client.GetStringAsync("/counter", AbortToken);
        var cached = await client.GetStringAsync("/counter", AbortToken);
        first.Should().Be("1");
        cached.Should().Be("1");

        // evict the tag through the store, exactly as a cross-cutting cache-invalidation handler would.
        // Cross a millisecond boundary first: the tag marker only invalidates entries whose CreatedAt strictly
        // predates it, and both are millisecond-precision.
        await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);
        var store = host.Services.GetRequiredService<IOutputCacheStore>();
        await store.EvictByTagAsync("products", AbortToken);

        var afterEvict = await client.GetStringAsync("/counter", AbortToken);
        afterEvict.Should().Be("2");
    }

    private async Task<IHost> _CreateHostAsync()
    {
        var cacheName = $"output-cache-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";
        var counter = 0;

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
            webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddOutputCache();
                    services.AddHeadlessCaching(setup =>
                    {
                        setup.UseRedis(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer);
                        setup.UseOutputCache(
                            options => options.CacheName = cacheName,
                            instance =>
                                instance.UseRedis(options =>
                                {
                                    options.ConnectionMultiplexer = fixture.ConnectionMultiplexer;
                                    options.KeyPrefix = keyPrefix;
                                })
                        );
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseOutputCache();
                    app.UseEndpoints(endpoints =>
                        endpoints
                            .MapGet(
                                "/counter",
                                async context =>
                                {
                                    var n = Interlocked.Increment(ref counter);
                                    await context.Response.WriteAsync(
                                        n.ToString(CultureInfo.InvariantCulture),
                                        context.RequestAborted
                                    );
                                }
                            )
                            .CacheOutput(policy => policy.Tag("products").Expire(TimeSpan.FromMinutes(5)))
                    );
                })
        );

        var host = builder.Build();
        await host.StartAsync(AbortToken);

        return host;
    }
}
