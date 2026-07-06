// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Headless.Redis.Testing;
using Headless.Testing.Testcontainers;
using StackExchange.Redis;

namespace Tests;

[CollectionDefinition(nameof(BclRedisFixture), DisableParallelization = false)]
public sealed class BclRedisFixture : HeadlessRedisFixture, ICollectionFixture<BclRedisFixture>
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
}
