// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Redis;
using Headless.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests.ReaderWriterLocks;

[Collection<RedisTestFixture>]
public sealed class RedisReaderWriterLockProviderTests(RedisTestFixture fixture) : TestBase
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    [Fact]
    public async Task should_allow_multiple_readers_and_release_on_dispose()
    {
        // given
        var provider = _CreateProvider();
        var resource = _NewResource();

        // when
        await using (var first = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken))
        await using (var second = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken))
        {
            // then
            first.LockId.Should().NotBe(second.LockId);
            (await provider.GetReaderCountAsync(resource, AbortToken)).Should().Be(2);
        }

        // and
        (await provider.IsReadLockedAsync(resource, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_prefer_queued_writer_over_new_reader()
    {
        // given
        var provider = _CreateProvider();
        var resource = _NewResource();
        await using var reader = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);

        // when
        var writerTask = provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(5) },
            AbortToken
        );
        await _EventuallyAsync(
            async () =>
            {
                var db = fixture.ConnectionMultiplexer.GetDatabase();
                var writerKeyValue = await db.StringGetAsync("{" + "distributed-lock:" + resource + "}:writer");

                return writerKeyValue.HasValue;
            }
        );
        var blockedReader = await provider.TryAcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );
        await reader.ReleaseAsync();
        await using var writer = await writerTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        blockedReader.Should().BeNull();
        writer.Should().NotBeNull();
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_auto_extend_write_lock()
    {
        // given
        var provider = _CreateProvider(
            new DistributedLockOptions
            {
                AutoExtensionCadenceFraction = 0.1,
                PollingCadenceFraction = 0.1,
            }
        );
        var resource = _NewResource();

        // when
        await using var writer = await provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(1),
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );
        await Task.Delay(TimeSpan.FromSeconds(3), AbortToken);

        // then
        writer.RenewalCount.Should().BeGreaterThan(0);
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();
    }

    private DistributedReaderWriterLockProvider _CreateProvider(DistributedLockOptions? options = null)
    {
        return new DistributedReaderWriterLockProvider(
            fixture.ReaderWriterLockStorage,
            outboxPublisher: null,
            options ?? new DistributedLockOptions(),
            new SnowflakeIdLongIdGenerator(),
            TimeProvider.System,
            LoggerFactory.CreateLogger<DistributedReaderWriterLockProvider>()
        );
    }

    private async Task _EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = TimeProvider.System.GetUtcNow() + TimeSpan.FromSeconds(5);

        while (TimeProvider.System.GetUtcNow() < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);
        }

        throw new TimeoutException("Condition was not met before the timeout elapsed.");
    }

    private string _NewResource()
    {
        return $"rw:{Faker.Random.AlphaNumeric(10)}";
    }
}
