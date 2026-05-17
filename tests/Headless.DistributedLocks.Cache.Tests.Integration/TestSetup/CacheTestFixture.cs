// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Headless.Testing.Testcontainers;
using StackExchange.Redis;

namespace Tests.TestSetup;

[CollectionDefinition(DisableParallelization = false)]
public sealed class CacheTestFixture : HeadlessRedisFixture, ICollectionFixture<CacheTestFixture>
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
        await ConnectionMultiplexer.FlushAllAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        ScriptsLoader.Dispose();
        await ConnectionMultiplexer.DisposeAsync();
        await base.DisposeAsyncCore();
    }
}
