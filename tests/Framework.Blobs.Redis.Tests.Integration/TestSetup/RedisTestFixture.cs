﻿// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests.TestSetup;

[CollectionDefinition]
public sealed class RedisTestFixture(IMessageSink messageSink)
    : ContainerFixture<RedisBuilder, RedisContainer>(messageSink),
        ICollectionFixture<RedisTestFixture>
{
    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

    protected override RedisBuilder Configure(RedisBuilder builder)
    {
        return base.Configure(builder).WithLabel("type", "redis_blobs").WithImage("redis:7.4").WithReuse(true);
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
