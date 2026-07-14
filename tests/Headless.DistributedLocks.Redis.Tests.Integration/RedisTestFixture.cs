// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.Redis;
using Headless.Redis;
using Headless.Redis.Testing;
using Headless.Testing.Testcontainers;
using StackExchange.Redis;

namespace Tests;

[CollectionDefinition(DisableParallelization = false)]
public sealed class RedisTestFixture : HeadlessRedisFixture, ICollectionFixture<RedisTestFixture>
{
    private HeadlessRedisScriptsLoader? _scriptLoader;

    public ConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

    public HeadlessRedisScriptsLoader ScriptsLoader => _scriptLoader!;

    internal RedisDistributedLockStorage LockStorage { get; private set; } = null!;

    internal RedisDistributedReadWriteLockStorage ReaderWriterLockStorage { get; private set; } = null!;

    internal RedisDistributedSemaphoreStorage SemaphoreStorage { get; private set; } = null!;

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var connectionString = Container.GetConnectionString() + ",allowAdmin=true";

        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await ConnectionMultiplexer.FlushAllAsync();

        _scriptLoader = new HeadlessRedisScriptsLoader(ConnectionMultiplexer);

        LockStorage = new(ConnectionMultiplexer, _scriptLoader);
        ReaderWriterLockStorage = new(ConnectionMultiplexer, _scriptLoader);
        SemaphoreStorage = new(ConnectionMultiplexer, _scriptLoader);
        await ConnectionMultiplexer.FlushAllAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _scriptLoader?.Dispose();
        if (ConnectionMultiplexer is not null)
        {
            await ConnectionMultiplexer.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }
}
