// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.ResourceLocks;
using Framework.ResourceLocks.Redis;
using Framework.ResourceLocks.Storage.RegularLocks;
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

    public IResourceLockStorage ResourceLockStorage { get; private set; } = null!;

    public IThrottlingResourceLockStorage ThrottlingResourceLockStorage { get; private set; } = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        var connectionString = Container.GetConnectionString();
        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        ResourceLockStorage = new RedisResourceLockStorage(ConnectionMultiplexer);
        ThrottlingResourceLockStorage = new RedisThrottlingResourceLockStorage(ConnectionMultiplexer);
    }
}
