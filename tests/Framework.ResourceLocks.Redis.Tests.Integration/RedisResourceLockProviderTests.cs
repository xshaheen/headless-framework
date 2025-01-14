using Foundatio.Messaging;
using Framework.Abstractions;
using Framework.Caching;
using Framework.Messaging;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Storage.RegularLocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class RedisResourceLockProviderTests : ResourceLockProviderTestsBase, IAsyncLifetime
{
    private static readonly SnowflakeIdLongIdGenerator _IdGenerator = new(1);
    private static readonly TimeProvider _TimeProvider = TimeProvider.System;
    private static readonly OptionsWrapper<ResourceLockOptions> _Options = new(new() { KeyPrefix = "test:" });
    private readonly RedisTestFixture _fixture;
    private readonly RedisMessageBus _redisMessageBus;
    private readonly MessageBusFoundatioAdapter _messageBusAdapter;
    private readonly ILogger<StorageResourceLockProvider> _logger;

    public RedisResourceLockProviderTests(RedisTestFixture fixture, ITestOutputHelper output)
        : base(output)
    {
        _fixture = fixture;
        _redisMessageBus = new(builder =>
            builder
                .Subscriber(fixture.ConnectionMultiplexer.GetSubscriber())
                .Topic("test-lock")
                .LoggerFactory(LoggerFactory)
                .Serializer(FoundationHelper.JsonSerializer)
        );
        _messageBusAdapter = new(_redisMessageBus, new SequentialAsStringGuidGenerator());
        _logger = LoggerFactory.CreateLogger<StorageResourceLockProvider>();
    }

    public async Task InitializeAsync()
    {
        await _fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _messageBusAdapter?.Dispose();
            _redisMessageBus?.Dispose();
        }
    }

    protected override IResourceLockProvider GetLockProvider()
    {
        return new StorageResourceLockProvider(
            _fixture.LockStorage,
            _messageBusAdapter,
            _IdGenerator,
            _TimeProvider,
            _Options,
            _logger
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
    public override Task should_acquire_locks_in_parallel()
    {
        return base.should_acquire_locks_in_parallel();
    }
}
