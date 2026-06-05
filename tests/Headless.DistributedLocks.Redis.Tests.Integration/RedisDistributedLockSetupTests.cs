// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.Redis;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Tests;

[Collection<RedisTestFixture>]
public sealed class RedisDistributedLockSetupTests(RedisTestFixture fixture)
{
    [Fact]
    public async Task AddRedisDistributedLock_should_register_and_run_mutex_script_initializer_on_host_start()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IConnectionMultiplexer>(fixture.ConnectionMultiplexer);
        builder.Services.AddRedisDistributedLock();

        using var host = builder.Build();

        // when
        await host.StartAsync(TestContext.Current.CancellationToken);
        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        await initializer.WaitForInitializationAsync(TestContext.Current.CancellationToken);

        // then
        initializer.IsInitialized.Should().BeTrue();

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void AddRedisDistributedLock_features_should_register_feature_specific_initializers()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(fixture.ConnectionMultiplexer);

        // when
        services.AddRedisDistributedLock();
        services.AddRedisDistributedSemaphore(static _ => { });
        services.AddRedisDistributedReadWriteLock(static _ => { });

        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IEnumerable<IInitializer>>().Should().HaveCount(3);
    }

    [Fact]
    public void AddRedisDistributedLock_should_fail_validation_when_options_are_invalid()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(fixture.ConnectionMultiplexer);
        services.AddRedisDistributedLock(options =>
        {
            options.MaxResourceNameLength = 0; // invalid per DistributedLockOptionsValidator
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () =>
            provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<Headless.DistributedLocks.DistributedLockOptions>>()
                .Value;

        // then
        act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>();
    }
}
