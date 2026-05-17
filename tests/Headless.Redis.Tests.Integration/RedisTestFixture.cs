// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Headless.Testing.Testcontainers;
using StackExchange.Redis;

namespace Tests;

[CollectionDefinition(nameof(RedisTestFixture), DisableParallelization = false)]
public sealed class RedisTestFixture : HeadlessRedisFixture, ICollectionFixture<RedisTestFixture>
{
    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

    public HeadlessRedisScriptsLoader ScriptsLoader { get; private set; } = null!;

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
