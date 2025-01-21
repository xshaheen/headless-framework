// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;
using Testcontainers.Xunit;

namespace Tests.TestSetup;

[CollectionDefinition(nameof(CacheTestFixture), DisableParallelization = false)]
public sealed class CacheTestFixture(IMessageSink messageSink)
    : ContainerFixture<RedisBuilder, RedisContainer>(messageSink),
        ICollectionFixture<CacheTestFixture>
{
    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

    protected override RedisBuilder Configure(RedisBuilder builder)
    {
        return base.Configure(builder).WithLabel("type", "resource_locks_cache").WithImage("redis:7.4").WithReuse(true);
    }

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        var connectionString = Container.GetConnectionString() + ",allowAdmin=true";

        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await ConnectionMultiplexer.FlushAllAsync();

        var scriptLoader = new FrameworkRedisScriptsLoader(ConnectionMultiplexer);
        await scriptLoader.LoadScriptsAsync();
        await ConnectionMultiplexer.FlushAllAsync();
    }

    protected override async Task DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();
        await ConnectionMultiplexer.DisposeAsync();
    }
}
