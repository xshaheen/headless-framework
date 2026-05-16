// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.Redis;
using Headless.Redis;
using Headless.Testing.Testcontainers;
using StackExchange.Redis;

namespace Tests;

[CollectionDefinition(DisableParallelization = false)]
public sealed class RedisTestFixture : HeadlessRedisFixture, ICollectionFixture<RedisTestFixture>
{
    private HeadlessRedisScriptsLoader? _scriptLoader;

    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

    public RedisDistributedLockStorage LockStorage { get; private set; } = null!;

    public RedisThrottlingDistributedLockStorage ThrottlingLockStorage { get; private set; } = null!;

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var connectionString = Container.GetConnectionString() + ",allowAdmin=true";

        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await ConnectionMultiplexer.FlushAllAsync();

        _scriptLoader = new HeadlessRedisScriptsLoader(ConnectionMultiplexer);
        await _scriptLoader.LoadScriptsAsync();

        LockStorage = new(ConnectionMultiplexer, _scriptLoader);
        ThrottlingLockStorage = new(ConnectionMultiplexer, _scriptLoader);

        await ConnectionMultiplexer.FlushAllAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _scriptLoader?.Dispose();
        await ConnectionMultiplexer.DisposeAsync();
        await base.DisposeAsyncCore();
    }
}
