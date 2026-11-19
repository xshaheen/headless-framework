// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Headless.Testing.Testcontainers;
using StackExchange.Redis;

namespace Tests;

[CollectionDefinition(nameof(RedisClusterCacheFixture), DisableParallelization = false)]
public sealed class RedisClusterCacheFixture : HeadlessRedisClusterFixture, ICollectionFixture<RedisClusterCacheFixture>
{
    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;
    public HeadlessRedisScriptsLoader ScriptsLoader { get; private set; } = null!;

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        ScriptsLoader = new HeadlessRedisScriptsLoader(ConnectionMultiplexer);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        ScriptsLoader?.Dispose();
        if (ConnectionMultiplexer is not null)
        {
            await ConnectionMultiplexer.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }
}
