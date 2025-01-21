// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Redis;
using Framework.ResourceLocks.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;
using Testcontainers.Xunit;

namespace Tests.TestSetup;

[CollectionDefinition(nameof(RedisTestFixture), DisableParallelization = false)]
public sealed class RedisTestFixture(IMessageSink messageSink)
    : ContainerFixture<RedisBuilder, RedisContainer>(messageSink),
        ICollectionFixture<RedisTestFixture>
{
    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

    public RedisResourceLockStorage LockStorage { get; private set; } = null!;

    public RedisThrottlingResourceLockStorage ThrottlingLockStorage { get; private set; } = null!;

    protected override RedisBuilder Configure(RedisBuilder builder)
    {
        return base.Configure(builder).WithLabel("type", "resource_locks_redis").WithImage("redis:7.4").WithReuse(true);
    }

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        var connectionString = Container.GetConnectionString() + ",allowAdmin=true";

        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await ConnectionMultiplexer.FlushAllAsync();

        var scriptLoader = new FrameworkRedisScriptsLoader(ConnectionMultiplexer);
        await scriptLoader.LoadScriptsAsync();

        LockStorage = new(ConnectionMultiplexer, scriptLoader);
        ThrottlingLockStorage = new(ConnectionMultiplexer, scriptLoader);

        await ConnectionMultiplexer.FlushAllAsync();
    }

    protected override async Task DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();
        await ConnectionMultiplexer.DisposeAsync();
    }
}
