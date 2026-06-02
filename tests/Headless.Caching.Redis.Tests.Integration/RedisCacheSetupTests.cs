// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection(nameof(RedisCacheFixture))]
public sealed class RedisCacheSetupTests(RedisCacheFixture fixture)
{
    [Fact]
    public async Task AddRedisCache_should_register_and_run_script_initializer_on_host_start()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRedisCache(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer);

        using var host = builder.Build();

        // when
        await host.StartAsync(TestContext.Current.CancellationToken);
        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        await initializer.WaitForInitializationAsync(TestContext.Current.CancellationToken);

        // then
        initializer.IsInitialized.Should().BeTrue();

        await host.StopAsync(TestContext.Current.CancellationToken);
    }
}
