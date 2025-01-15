using Foundatio.Caching;
using Foundatio.Messaging;
using Framework.Caching;
using Framework.Messaging;
using Framework.Threading;
using Tests.Lock;
using Tests.Storage;

namespace Tests.Tests;

public class RedisFoundationLockProviderTests : ResourceLockProviderTestsBase
{
    private readonly RedisMessageBus _redisMessageBus;
    private readonly RedisCacheClient _redisStorage;
    private readonly FoundationLockStorageAdapter _redisLockStorage;

    public RedisFoundationLockProviderTests(ITestOutputHelper output)
        : base(output)
    {
        var muxer = SharedConnection.GetMuxer(LoggerFactory);
        Async.RunSync(muxer.FlushAllAsync);

        _redisMessageBus = new RedisMessageBus(o =>
            o.Subscriber(muxer.GetSubscriber()).Topic("test-lock").LoggerFactory(LoggerFactory)
        );

        _redisStorage = new RedisCacheClient(o =>
            o.ConnectionMultiplexer(muxer).LoggerFactory(LoggerFactory).Serializer(FoundationHelper.JsonSerializer)
        );

        _redisLockStorage = new FoundationLockStorageAdapter(_redisStorage);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _redisMessageBus?.Dispose();
            _redisStorage?.Dispose();
            _redisLockStorage?.Dispose();
        }
    }

    protected override ILockProvider GetLockProvider()
    {
        return new CacheLockProvider(LongGenerator, _redisLockStorage, _redisMessageBus, TimeProvider, LoggerFactory);
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
}
