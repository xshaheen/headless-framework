// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Headless.Testing.Testcontainers;
using StackExchange.Redis;

namespace Tests;

[CollectionDefinition(nameof(RedisCacheFixture), DisableParallelization = false)]
public sealed class RedisCacheFixture : HeadlessRedisFixture, ICollectionFixture<RedisCacheFixture>
{
    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;
    public HeadlessRedisScriptsLoader ScriptsLoader { get; private set; } = null!;

    /// <summary>The container's connection string with admin commands allowed, for a test that needs its own connection.</summary>
    public string ConnectionString { get; private set; } = null!;

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        ConnectionString = Container.GetConnectionString() + ",allowAdmin=true";

        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        await FlushAllAsync();

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
