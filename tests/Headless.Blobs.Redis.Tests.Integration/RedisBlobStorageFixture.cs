// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

[CollectionDefinition(DisableParallelization = true)]
public sealed class RedisBlobStorageFixture(IMessageSink messageSink)
    : ContainerFixture<RedisBuilder, RedisContainer>(messageSink),
        ICollectionFixture<RedisBlobStorageFixture>
{
    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

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
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();
        await ConnectionMultiplexer.DisposeAsync();
    }
}
