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

    [Fact]
    public async Task should_fire_handle_lost_token_when_read_lock_ttl_expires()
    {
        // given - short TTL with Monitor mode. Redis evicts the reader entry; the validate probe
        // then returns false and the monitor fires HandleLostToken.
        var provider = _CreateProvider(
            new DistributedLockOptions
            {
                PollingCadenceFraction = 0.1,
            }
        );
        var resource = _NewResource();
        await using var handle = await provider.AcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(1),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );

        // when
        await _EventuallyAsync(() => Task.FromResult(handle.HandleLostToken.IsCancellationRequested));

        // then
        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_fire_handle_lost_token_when_write_lock_ttl_expires()
    {
        // given
        var provider = _CreateProvider(
            new DistributedLockOptions
            {
                PollingCadenceFraction = 0.1,
            }
        );
        var resource = _NewResource();
        await using var handle = await provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(1),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );

        // when
        await _EventuallyAsync(() => Task.FromResult(handle.HandleLostToken.IsCancellationRequested));

        // then
        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_distributed_lock_exception_when_acquire_read_storage_keeps_returning_false()
    {
        // given - a competing writer holds the resource, so the reader can never acquire.
        var provider = _CreateProvider();
        var resource = _NewResource();
        await using var writer = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);

        // when
        var act = async () =>
            await provider.AcquireReadLockAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMilliseconds(200) },
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<DistributedLockException>();
    }

    [Fact]
    public async Task should_throw_distributed_lock_exception_when_acquire_write_storage_keeps_returning_false()
    {
        // given - a reader holds the resource, so the writer is queued and times out.
        var provider = _CreateProvider();
        var resource = _NewResource();
        await using var reader = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);

        // when
        var act = async () =>
            await provider.AcquireWriteLockAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMilliseconds(500) },
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<DistributedLockException>();
    }

    [Fact]
    public async Task should_clear_writer_waiting_marker_when_try_acquire_write_is_cancelled()
    {
        // given - reader holds the resource; the writer plants the waiting marker then is
        // cancelled. The provider cleanup must remove the marker so the writer key is empty.
        var provider = _CreateProvider();
        var resource = _NewResource();
        await using var reader = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        // when
        var act = async () =>
            await provider.TryAcquireWriteLockAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(5) },
                cts.Token
            );

        // then - the storage call surfaces OperationCanceled; the writer key must be empty.
        await act.Should().ThrowAsync<OperationCanceledException>();

        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var writerKeyValue = await db.StringGetAsync("{" + "distributed-lock:" + resource + "}:writer");
        writerKeyValue.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_leave_lock_held_when_release_on_dispose_is_false()
    {
        // given
        var provider = _CreateProvider();
        var resource = _NewResource();
        var handle = await provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { ReleaseOnDispose = false },
            AbortToken
        );

        // when
        await handle.DisposeAsync();

        // then - still locked. The handle's ReleaseAsync is the public release entry point; it
        // skips the early-return when _isReleased is false (DisposeAsync with releaseOnDispose=false
        // never sets it), so we re-acquire and dispose to cleanly tear down for the next test.
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();

        // and - explicit release via the handle succeeds and clears it.
        await handle.ReleaseAsync();
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_be_idempotent_for_stale_release()
    {
        // given - acquire, release, then call release again on a stale id.
        var provider = _CreateProvider();
        var resource = _NewResource();
        var handle = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);
        await handle.ReleaseAsync();

        // when / then - second release MUST NOT throw. We invoke ReleaseAsync on the same handle;
        // its internal guard short-circuits cleanly so the underlying storage call is a no-op.
        var act = async () => await handle.ReleaseAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_queue_second_writer_and_unblock_after_first_releases()
    {
        // given
        var provider = _CreateProvider();
        var resource = _NewResource();
        var firstWriter = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);

        // when - second writer is queued, blocked by first.
        var secondWriterTask = provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(5) },
            AbortToken
        );
        await Task.Delay(TimeSpan.FromMilliseconds(200), AbortToken);
        secondWriterTask.IsCompleted.Should().BeFalse();

        // and - release first.
        await firstWriter.ReleaseAsync();

        // then - second now unblocks.
        await using var secondWriter = await secondWriterTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        secondWriter.Should().NotBeNull();
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
