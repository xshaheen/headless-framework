// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Redis.Testing;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Tests;

[CollectionDefinition(nameof(RedisMembershipFixture), DisableParallelization = false)]
public sealed class RedisMembershipFixture
    : HeadlessRedisFixture,
        ICollectionFixture<RedisMembershipFixture>,
        ICoordinationFixture
{
    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var connectionString = Container.GetConnectionString() + ",allowAdmin=true";
        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await ConnectionMultiplexer.FlushAllAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (ConnectionMultiplexer is not null)
        {
            await ConnectionMultiplexer.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    public void ConfigureProvider(IServiceCollection services, HeadlessCoordinationSetupBuilder setup)
    {
        services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer);
        setup.UseRedis(static options =>
        {
            options.RedisCleanupInterval = TimeSpan.FromMilliseconds(100);
            options.RedisKnownNodeRetention = TimeSpan.FromMilliseconds(600);
        });
    }
}
