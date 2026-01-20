using Foundatio.Messaging;
using Framework.Caching;
using Framework.Messages;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Cache;
using Framework.Serializer;
using Microsoft.Extensions.Logging;
using Tests.TestSetup;

namespace Tests;

[Collection<CacheTestFixture>]
public class RedisResourceLockProviderTests : ResourceLockProviderTestsBase
{
    private readonly RedisMessageBus _foundatioMessageBus;
    private readonly FoundatioMessageBusAdapter _bus;
    private readonly RedisCachingFoundatioAdapter _cache;
    private readonly CacheResourceLockStorage _storage;

    public RedisResourceLockProviderTests(CacheTestFixture fixture)
    {
        _foundatioMessageBus = new RedisMessageBus(o =>
            o.Subscriber(fixture.ConnectionMultiplexer.GetSubscriber()).Topic("test-lock").LoggerFactory(LoggerFactory)
        );

        _bus = new(_foundatioMessageBus, GuidGenerator);

        _cache = new RedisCachingFoundatioAdapter(
            new SystemJsonSerializer(),
            TimeProvider,
            new RedisCacheOptions { ConnectionMultiplexer = fixture.ConnectionMultiplexer }
        );

        _storage = new CacheResourceLockStorage(_cache);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _foundatioMessageBus?.Dispose();
        _bus?.Dispose();
        _cache?.Dispose();
        return base.DisposeAsyncCore();
    }

    protected override IResourceLockProvider GetLockProvider()
    {
        return new ResourceLockProvider(
            _storage,
            _bus,
            Options,
            LongGenerator,
            TimeProvider,
            LoggerFactory.CreateLogger<ResourceLockProvider>()
        );
    }

    [Fact]
    public override Task should_lock_with_try_acquire()
    {
        return base.should_lock_with_try_acquire();
    }

    [Fact]
    public override Task should_not_acquire_when_already_locked()
    {
        return base.should_not_acquire_when_already_locked();
    }

    [Fact]
    public override Task should_obtain_multiple_locks()
    {
        return base.should_obtain_multiple_locks();
    }

    [Fact]
    public override async Task should_release_lock_multiple_times()
    {
        await base.should_release_lock_multiple_times();
    }

    [Fact]
    public override Task should_timeout_when_try_to_lock_acquired_resource()
    {
        return base.should_timeout_when_try_to_lock_acquired_resource();
    }

    [Fact]
    public override Task should_lock_one_at_a_time_async()
    {
        return base.should_lock_one_at_a_time_async();
    }

    [Fact]
    public override Task should_acquire_and_release_locks_async()
    {
        return base.should_acquire_and_release_locks_async();
    }

    [Fact]
    public override Task should_acquire_one_at_a_time_parallel()
    {
        return base.should_acquire_one_at_a_time_parallel();
    }

    [Fact]
    public override Task should_acquire_locks_in_parallel()
    {
        return base.should_acquire_locks_in_parallel();
    }

    [Fact]
    public override Task should_acquire_locks_in_sync()
    {
        return base.should_acquire_locks_in_sync();
    }
}
