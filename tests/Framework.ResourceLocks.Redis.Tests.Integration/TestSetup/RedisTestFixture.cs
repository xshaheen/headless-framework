// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Caching;
using Framework.ResourceLocks.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Testcontainers.Xunit;

namespace Tests.TestSetup;

[CollectionDefinition(nameof(RedisTestFixture))]
public sealed class RedisTestFixture(IMessageSink messageSink)
    : ContainerFixture<RedisBuilder, RedisContainer>(messageSink),
        ICollectionFixture<RedisTestFixture>
{
    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

    public RedisResourceLockStorage LockStorage { get; private set; } = null!;

    public RedisThrottlingResourceLockStorage ThrottlingLockStorage { get; private set; } = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        var connectionString = Container.GetConnectionString();
        // const string connectionString = "127.0.0.1:7006,allowAdmin=true";
        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await ConnectionMultiplexer.FlushAllAsync();

        LockStorage = new(ConnectionMultiplexer, NullLogger<RedisResourceLockStorage>.Instance);
        await LockStorage.LoadScriptsAsync();

        ThrottlingLockStorage = new(ConnectionMultiplexer, NullLogger<RedisThrottlingResourceLockStorage>.Instance);
        await ThrottlingLockStorage.LoadScriptsAsync();
    }

    protected override async Task DisposeAsyncCore()
    {
        await ConnectionMultiplexer.DisposeAsync();
    }
}
