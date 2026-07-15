// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Hosting.Initialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Tests;

[Collection<RedisTestFixture>]
public sealed class RedisDistributedLockSetupTests(RedisTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_register_and_run_redis_initializers_on_host_start_when_add_headless_distributed_locks()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IConnectionMultiplexer>(fixture.ConnectionMultiplexer);
        builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        var initializers = host.Services.GetRequiredService<IEnumerable<IInitializer>>().ToList();

        foreach (var initializer in initializers)
        {
            await initializer.WaitForInitializationAsync(AbortToken);
        }

        // then
        initializers.Should().HaveCount(3);
        initializers.Should().AllSatisfy(initializer => initializer.IsInitialized.Should().BeTrue());

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public void should_register_feature_specific_initializers_when_add_headless_distributed_locks()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(fixture.ConnectionMultiplexer);

        // when
        services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IEnumerable<IInitializer>>().Should().HaveCount(3);
    }

    [Fact]
    public void should_fail_validation_when_add_headless_distributed_locks_options_are_invalid()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(fixture.ConnectionMultiplexer);
        services.AddHeadlessDistributedLocks(setup =>
        {
            setup.ConfigureOptions(options =>
            {
                options.MaxResourceNameLength = 0; // invalid per DistributedLockOptionsValidator
            });
            setup.UseRedis();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DistributedLockOptions>>().Value;

        // then
        act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>();
    }
}
