// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Caching;
using Framework.ResourceLocks.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Tests.TestSetup;

[CollectionDefinition(nameof(RedisTestFixture), DisableParallelization = false)]
public sealed class RedisTestFixture // (IMessageSink messageSink)
    : IAsyncLifetime, // ContainerFixture<RedisBuilder, RedisContainer>(messageSink),
        ICollectionFixture<RedisTestFixture>
{
    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

    public RedisResourceLockStorage LockStorage { get; private set; } = null!;

    public RedisThrottlingResourceLockStorage ThrottlingLockStorage { get; private set; } = null!;

    // protected override async Task InitializeAsync()
    public async Task InitializeAsync()
    {
        // await base.InitializeAsync();
        // var connectionString = Container.GetConnectionString() + ",allowAdmin=true";

        const string connectionString = "127.0.0.1:7006,allowAdmin=true";
        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await ConnectionMultiplexer.FlushAllAsync();

        LockStorage = new(ConnectionMultiplexer, NullLogger<RedisResourceLockStorage>.Instance);
        await LockStorage.LoadScriptsAsync();

        ThrottlingLockStorage = new(ConnectionMultiplexer, NullLogger<RedisThrottlingResourceLockStorage>.Instance);
        await ThrottlingLockStorage.LoadScriptsAsync();
    }

    // protected override async Task DisposeAsyncCore()
    public async Task DisposeAsync()
    {
        await ConnectionMultiplexer.DisposeAsync();
    }
}
