// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests.TestSetup;

[CollectionDefinition(DisableParallelization = false)]
public sealed class CacheTestFixture(IMessageSink messageSink)
    : ContainerFixture<RedisBuilder, RedisContainer>(messageSink),
        ICollectionFixture<CacheTestFixture>
{
    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

    protected override RedisBuilder Configure()
    {
        return base.Configure().WithLabel("type", "resource_locks_cache").WithImage("redis:7-alpine").WithReuse(true);
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var connectionString = Container.GetConnectionString() + ",allowAdmin=true";

        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await ConnectionMultiplexer.FlushAllAsync();

        var scriptLoader = new HeadlessRedisScriptsLoader(ConnectionMultiplexer);
        await scriptLoader.LoadScriptsAsync();
        await ConnectionMultiplexer.FlushAllAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();
        await ConnectionMultiplexer.DisposeAsync();
    }
}
