// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection(nameof(BclRedisFixture))]
public sealed class SessionRoundTripTests(BclRedisFixture fixture) : TestBase
{
    [Fact]
    public async Task should_round_trip_through_headless_distributed_cache_when_aspnet_core_session()
    {
        var cacheName = $"session-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";

        using var host = await _CreateHostAsync(cacheName, keyPrefix, idleTimeout: TimeSpan.FromSeconds(3));
        using var client = host.GetTestClient();

        using var writeResponse = await client.GetAsync("/write", AbortToken);
        writeResponse.EnsureSuccessStatusCode();
        var cookie = writeResponse.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        using var readRequest = new HttpRequestMessage(HttpMethod.Get, "/read");
        readRequest.Headers.Add("Cookie", cookie);
        using var readResponse = await client.SendAsync(readRequest, AbortToken);

        readResponse.EnsureSuccessStatusCode();
        var body = await readResponse.Content.ReadAsStringAsync(AbortToken);
        body.Should().Be("v1");
    }

    [Fact]
    public async Task should_extend_idle_lifetime_on_read_when_aspnet_core_session()
    {
        var cacheName = $"session-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";
        var idleTimeout = TimeSpan.FromMilliseconds(700);

        using var host = await _CreateHostAsync(cacheName, keyPrefix, idleTimeout);
        using var client = host.GetTestClient();

        using var writeResponse = await client.GetAsync("/write", AbortToken);
        writeResponse.EnsureSuccessStatusCode();
        var cookie = writeResponse.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(450), AbortToken);

        using (var readRequest = new HttpRequestMessage(HttpMethod.Get, "/read"))
        {
            readRequest.Headers.Add("Cookie", cookie);
            using var readResponse = await client.SendAsync(readRequest, AbortToken);
            readResponse.EnsureSuccessStatusCode();
            var body = await readResponse.Content.ReadAsStringAsync(AbortToken);
            body.Should().Be("v1");
        }

        await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(450), AbortToken);

        using (var readRequest = new HttpRequestMessage(HttpMethod.Get, "/read"))
        {
            readRequest.Headers.Add("Cookie", cookie);
            using var readResponse = await client.SendAsync(readRequest, AbortToken);
            readResponse.EnsureSuccessStatusCode();
            var body = await readResponse.Content.ReadAsStringAsync(AbortToken);
            body.Should().Be("v1");
        }
    }

    private async Task<IHost> _CreateHostAsync(string cacheName, string keyPrefix, TimeSpan idleTimeout)
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
            webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSession(options => options.IdleTimeout = idleTimeout);
                    services.AddHeadlessCaching(setup =>
                    {
                        setup.UseRedis(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer);
                        setup.UseBclCache(
                            options =>
                            {
                                options.CacheName = cacheName;
                                options.DefaultAbsoluteExpiration = TimeSpan.FromSeconds(10);
                            },
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
                    app.UseSession();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet(
                            "/write",
                            async context =>
                            {
                                context.Session.SetString("k", "v1");
                                await context.Session.CommitAsync(context.RequestAborted);
                                await context.Response.WriteAsync("ok", context.RequestAborted);
                            }
                        );
                        endpoints.MapGet(
                            "/read",
                            async context =>
                            {
                                await context.Response.WriteAsync(
                                    context.Session.GetString("k") ?? "missing",
                                    context.RequestAborted
                                );
                            }
                        );
                    });
                })
        );

        var host = builder.Build();
        await host.StartAsync(AbortToken);

        return host;
    }
}
