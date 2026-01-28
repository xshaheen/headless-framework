// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

[CollectionDefinition(nameof(RedisTestFixture), DisableParallelization = false)]
public sealed class RedisTestFixture(IMessageSink messageSink)
    : ContainerFixture<RedisBuilder, RedisContainer>(messageSink),
        ICollectionFixture<RedisTestFixture>
{
    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

    public HeadlessRedisScriptsLoader ScriptsLoader { get; private set; } = null!;

    protected override RedisBuilder Configure()
    {
        return base.Configure().WithImage("redis:7-alpine");
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var connectionString = Container.GetConnectionString() + ",allowAdmin=true";

        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await ConnectionMultiplexer.FlushAllAsync();

        ScriptsLoader = new HeadlessRedisScriptsLoader(ConnectionMultiplexer);
        await ScriptsLoader.LoadScriptsAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();
        await ConnectionMultiplexer.DisposeAsync();
    }
}
