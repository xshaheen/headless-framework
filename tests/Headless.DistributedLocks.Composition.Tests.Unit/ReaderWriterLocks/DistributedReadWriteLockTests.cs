// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless;
using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.ReaderWriterLocks;

public sealed class DistributedReadWriteLockTests : TestBase
{
    // The backoff is at most a few seconds while the acquire timeout these tests use is 30s, so any shorter timer
    // created after the waiter starts is its backoff wait.
    private static readonly TimeSpan _AcquireTimeout = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IGuidGenerator _guidGenerator = Substitute.For<IGuidGenerator>();

    private DistributedReadWriteLock _CreateProvider(
        IDistributedReadWriteLockStorage? storage = null,
        IBus? bus = null,
        DistributedLockOptions? options = null,
        FakeTimeProvider? timeProvider = null
    )
    {
        _guidGenerator.Create().Returns(_ => Guid.NewGuid());
        timeProvider ??= _timeProvider;

        return new DistributedReadWriteLock(
            storage ?? new InMemoryDistributedReadWriteLockStorage(timeProvider),
            bus,
            options ?? new DistributedLockOptions(),
            _guidGenerator,
            timeProvider,
            LoggerFactory.CreateLogger<DistributedReadWriteLock>()
        );
    }

    private static Task<TimeSpan> _WaitForBackoffArmedAsync(TimerRecordingTimeProvider timeProvider)
    {
        return timeProvider.WaitForTimerAsync(dueTime => dueTime < TimeSpan.FromSeconds(10), AbortToken);
    }

    [Fact]
    public async Task should_wake_write_waiter_without_advancing_time_when_release_signal_arrives()
    {
        // given - a held write lock and a write waiter sleeping on its backoff
        var timeProvider = new TimerRecordingTimeProvider();
        var provider = _CreateProvider(timeProvider: timeProvider);
        var resource = Faker.Random.AlphaNumeric(10);
        var holder = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);

        var waiterTask = provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = _AcquireTimeout },
            AbortToken
        );
        await _WaitForBackoffArmedAsync(timeProvider);

        // when - the holder releases and the release signal arrives; fake time never moves
        await holder.ReleaseAsync();
        ((ICanReceiveLockReleased)provider).OnLockReleased(new DistributedLockReleased(resource, holder.LeaseId));

        // then
        await using var lease = await waiterTask.Bounded();
        lease.Should().NotBeNull();
    }

    [Fact]
    public async Task should_wake_read_waiter_without_advancing_time_when_writer_release_signal_arrives()
    {
        // given - a held write lock and a read waiter sleeping on its backoff
        var timeProvider = new TimerRecordingTimeProvider();
        var provider = _CreateProvider(timeProvider: timeProvider);
        var resource = Faker.Random.AlphaNumeric(10);
        var writer = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);

        var readerTask = provider.TryAcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = _AcquireTimeout },
            AbortToken
        );
        await _WaitForBackoffArmedAsync(timeProvider);

        // when
        await writer.ReleaseAsync();
        ((ICanReceiveLockReleased)provider).OnLockReleased(new DistributedLockReleased(resource, writer.LeaseId));

        // then
        await using var lease = await readerTask.Bounded();
        lease.Should().NotBeNull();
    }

    [Fact]
    public async Task should_throw_operation_canceled_when_caller_cancels_during_backoff_wait()
    {
        // given - a write waiter sleeping on its backoff behind a held write lock
        var timeProvider = new TimerRecordingTimeProvider();
        var provider = _CreateProvider(timeProvider: timeProvider);
        var resource = Faker.Random.AlphaNumeric(10);
        await using var holder = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);

        using var callerCts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var waiterTask = provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = _AcquireTimeout },
            callerCts.Token
        );
        await _WaitForBackoffArmedAsync(timeProvider);

        // when
        await callerCts.CancelAsync();

        // then - a caller cancel still surfaces as a cancellation, never as a null "not acquired" result
        var act = async () => await waiterTask.Bounded();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_not_enforce_waiter_caps_when_reader_writer_waiters_exceed_them()
    {
        // given - caps of one waiter and one waiting resource, and two readers queued behind a writer
        var timeProvider = new TimerRecordingTimeProvider();
        var provider = _CreateProvider(
            options: new DistributedLockOptions { MaxWaitersPerResource = 1, MaxConcurrentWaitingResources = 1 },
            timeProvider: timeProvider
        );
        var resource = Faker.Random.AlphaNumeric(10);
        var writer = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);
        var acquireOptions = new DistributedLockAcquireOptions { AcquireTimeout = _AcquireTimeout };

        var firstReader = provider.TryAcquireReadLockAsync(resource, acquireOptions, AbortToken);
        var secondReader = provider.TryAcquireReadLockAsync(resource, acquireOptions, AbortToken);
        await _WaitForBackoffArmedAsync(timeProvider);
        await _WaitForBackoffArmedAsync(timeProvider);

        // when - the writer releases and both backoffs elapse
        await writer.ReleaseAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        // then - both waiters were admitted to the wait and both acquire
        await using var firstLease = await firstReader.Bounded();
        await using var secondLease = await secondReader.Bounded();
        firstLease.Should().NotBeNull();
        secondLease.Should().NotBeNull();
    }

    private DistributedReadWriteLock _CreateProviderWithLogger(
        IDistributedReadWriteLockStorage storage,
        ILogger<DistributedReadWriteLock> logger
    )
    {
        _guidGenerator.Create().Returns(_ => Guid.NewGuid());
        return new DistributedReadWriteLock(
            storage,
            bus: null,
            new DistributedLockOptions(),
            _guidGenerator,
            _timeProvider,
            logger
        );
    }

    [Fact]
    public async Task should_return_without_throwing_when_release_exceeds_dispose_timeout()
    {
        // given — write-release hangs forever in storage. DisposeTimeout bounds the release so
        // shutdown is never blocked; on timeout the release returns without throwing and the
        // record TTL is the eventual-consistency fallback.
        var storage = Substitute.For<IDistributedReadWriteLockStorage>();
        storage
            .ReleaseWriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask(new TaskCompletionSource().Task));

        var provider = _CreateProvider(
            storage,
            options: new DistributedLockOptions { DisposeTimeout = TimeSpan.FromSeconds(5) }
        );

        // when — run release and advance time past the dispose timeout
        var releaseTask = provider.ReleaseAsync(ReaderWriterLockMode.Write, "resource", "lease-1", AbortToken);

        for (var i = 0; i < 10; i++)
        {
            await Task.Yield();
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        var act = async () => await releaseTask.Bounded();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_publish_release_signal_directly()
    {
        // given
        var storage = Substitute.For<IDistributedReadWriteLockStorage>();
        storage
            .ReleaseWriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        var bus = Substitute.For<IBus>();
        var provider = _CreateProvider(storage, bus);
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.AlphaNumeric(10);

        // when
        await provider.ReleaseAsync(ReaderWriterLockMode.Write, resource, leaseId, AbortToken);

        // then
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<DistributedLockReleased>(message => message.Resource == resource && message.LeaseId == leaseId),
                Arg.Is<PublishOptions?>(options => options!.DeliveryMode == DeliveryMode.Direct),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_log_safety_deadline_eventid_and_tag_metric_stalled_when_write_deadline_fires()
    {
        // given — write-lock storage that hangs so the non-blocking safety deadline ends the attempt
        var storage = Substitute.For<IDistributedReadWriteLockStorage>();
        storage
            .TryAcquireWriteAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => new ValueTask<bool>(_HangUntilCancelledAsync(ci.ArgAt<CancellationToken>(5))));

        var captured = new List<int>();
        using var meterListener = new MeterListener();
        var failedReasons = DistributedLockTestSupport.CaptureFailedReasons(meterListener, "headless.lock.failed");
        var provider = _CreateProviderWithLogger(
            storage,
            new DistributedLockTestSupport.CapturingLogger<DistributedReadWriteLock>(captured)
        );
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var acquireTask = provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            CancellationToken.None
        );
        _timeProvider.Advance(TimeSpan.FromSeconds(DistributedLockTestSupport.NonBlockingAcquireDeadlineSeconds + 1));
        // Parked wait (not a busy spin): the safety CTS fired by Advance cancels the hung storage
        // call, so the acquire completes promptly. WaitAsync fails fast if it ever hangs.
        (await acquireTask.WaitAsync(TimeSpan.FromSeconds(30), AbortToken))
            .Should()
            .BeNull();

        // then
        captured.Should().Contain(DistributedLockTestSupport.SafetyDeadlineFiredEventId);
        failedReasons.Should().Contain(DistributedLockMetrics.ReasonStalled);
    }

    [Fact]
    public async Task should_log_safety_deadline_eventid_and_tag_metric_stalled_when_read_deadline_fires()
    {
        // given — read-lock storage that hangs; the read path shares _TryAcquireOnceAsync with write
        // but skips writer-marker cleanup, so it needs its own coverage.
        var storage = Substitute.For<IDistributedReadWriteLockStorage>();
        storage
            .TryAcquireReadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => new ValueTask<bool>(_HangUntilCancelledAsync(ci.ArgAt<CancellationToken>(3))));

        var captured = new List<int>();
        using var meterListener = new MeterListener();
        var failedReasons = DistributedLockTestSupport.CaptureFailedReasons(meterListener, "headless.lock.failed");
        var provider = _CreateProviderWithLogger(
            storage,
            new DistributedLockTestSupport.CapturingLogger<DistributedReadWriteLock>(captured)
        );
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var acquireTask = provider.TryAcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            CancellationToken.None
        );
        _timeProvider.Advance(TimeSpan.FromSeconds(DistributedLockTestSupport.NonBlockingAcquireDeadlineSeconds + 1));
        // Parked wait (not a busy spin): the safety CTS fired by Advance cancels the hung storage
        // call, so the acquire completes promptly. WaitAsync fails fast if it ever hangs.
        (await acquireTask.WaitAsync(TimeSpan.FromSeconds(30), AbortToken))
            .Should()
            .BeNull();

        // then
        captured.Should().Contain(DistributedLockTestSupport.SafetyDeadlineFiredEventId);
        failedReasons.Should().Contain(DistributedLockMetrics.ReasonStalled);
    }

    [Fact]
    public async Task should_not_log_safety_deadline_eventid_and_tag_metric_contended_when_write_is_held()
    {
        // given — storage that promptly returns "not acquired" (routine contention, no stall)
        var storage = Substitute.For<IDistributedReadWriteLockStorage>();
        storage
            .TryAcquireWriteAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(false));

        var captured = new List<int>();
        using var meterListener = new MeterListener();
        var failedReasons = DistributedLockTestSupport.CaptureFailedReasons(meterListener, "headless.lock.failed");
        var provider = _CreateProviderWithLogger(
            storage,
            new DistributedLockTestSupport.CapturingLogger<DistributedReadWriteLock>(captured)
        );
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            CancellationToken.None
        );

        // then — routine contention tags `contended`; the safety-deadline EventId stays silent
        result.Should().BeNull();
        captured.Should().NotContain(DistributedLockTestSupport.SafetyDeadlineFiredEventId);
        failedReasons.Should().Contain(DistributedLockMetrics.ReasonContended);
    }

    private static async Task<bool> _HangUntilCancelledAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        return false; // Unreachable — Task.Delay throws on cancellation.
    }

    [Fact]
    public async Task should_acquire_multiple_readers_for_same_resource()
    {
        // given
        var storage = new InMemoryDistributedReadWriteLockStorage(_timeProvider);
        var provider = _CreateProvider(storage);
        var resource = Faker.Random.AlphaNumeric(10);
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        _guidGenerator.Create().Returns(guid, Guid.NewGuid());

        // when
        await using var first = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);
        await using var second = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);

        // then
        first.Resource.Should().Be(resource);
        first.LeaseId.Should().Be("00112233445566778899aabbccddeeff");
        second.Resource.Should().Be(resource);
        (await provider.GetReaderCountAsync(resource, AbortToken)).Should().Be(2);
    }

    [Fact]
    public async Task should_dispatch_read_renew_and_release_to_read_storage_methods()
    {
        // given
        var storage = Substitute.For<IDistributedReadWriteLockStorage>();
        storage
            .TryAcquireReadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        storage
            .TryExtendReadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        var provider = _CreateProvider(storage);
        var resource = Faker.Random.AlphaNumeric(10);
        var scopedResource = $"distributed-lock:{resource}";

        // when
        await using var handle = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);
        var renewed = await handle.RenewAsync(cancellationToken: AbortToken);
        await handle.ReleaseAsync();

        // then
        renewed.Should().BeTrue();
        await storage.Received(1).TryExtendReadAsync(scopedResource, handle.LeaseId, Arg.Any<TimeSpan?>(), AbortToken);
        await storage.Received(1).ReleaseReadAsync(scopedResource, handle.LeaseId, Arg.Any<CancellationToken>());
        await storage
            .DidNotReceive()
            .TryExtendWriteAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_dispatch_write_renew_and_release_to_write_storage_methods()
    {
        // given
        var storage = Substitute.For<IDistributedReadWriteLockStorage>();
        storage
            .TryAcquireWriteAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        storage
            .TryExtendWriteAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        var provider = _CreateProvider(storage);
        var resource = Faker.Random.AlphaNumeric(10);
        var scopedResource = $"distributed-lock:{resource}";

        // when
        await using var handle = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);
        var renewed = await handle.RenewAsync(cancellationToken: AbortToken);
        await handle.ReleaseAsync();

        // then
        renewed.Should().BeTrue();
        await storage.Received(1).TryExtendWriteAsync(scopedResource, handle.LeaseId, Arg.Any<TimeSpan?>(), AbortToken);
        await storage.Received(1).ReleaseWriteAsync(scopedResource, handle.LeaseId, Arg.Any<CancellationToken>());
        await storage
            .DidNotReceive()
            .TryExtendReadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_cleanup_writer_waiting_marker_when_try_write_times_out()
    {
        // given
        var storage = new ReleaseObservingReaderWriterLockStorage(
            new InMemoryDistributedReadWriteLockStorage(_timeProvider)
        );
        var provider = _CreateProvider(storage);
        var resource = Faker.Random.AlphaNumeric(10);
        await storage.TryAcquireReadAsync(
            $"distributed-lock:{resource}",
            "reader-1",
            TimeSpan.FromSeconds(10),
            AbortToken
        );

        // when
        var result = await provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        // then
        result.Should().BeNull();
        storage.WriteReleaseCount.Should().Be(1);
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_return_null_when_non_zero_write_acquire_timeout_elapses()
    {
        // given
        var storage = new ReleaseObservingReaderWriterLockStorage(
            new InMemoryDistributedReadWriteLockStorage(_timeProvider)
        );
        var provider = _CreateProvider(storage);
        var resource = Faker.Random.AlphaNumeric(10);
        await storage.TryAcquireReadAsync(
            $"distributed-lock:{resource}",
            "reader-1",
            TimeSpan.FromSeconds(10),
            AbortToken
        );

        // when
        var acquireTask = provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(1) },
            AbortToken
        );
        await Task.Yield();
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        var result = await acquireTask.Bounded();

        // then
        result.Should().BeNull();
        storage.WriteReleaseCount.Should().BePositive();
    }

    [Fact]
    public async Task should_cleanup_writer_waiting_marker_when_try_write_storage_observes_caller_cancellation()
    {
        // given - try-once path; storage observes caller cancellation mid-call, so the Lua may
        // have planted the writer-waiting marker before the OCE surfaced. The fix in
        // _TryAcquireOnceAsync MUST issue the idempotent cleanup before rethrowing.
        var storage = new InMemoryDistributedReadWriteLockStorage(_timeProvider);
        var releaseObserver = new ReleaseObservingReaderWriterLockStorage(storage);
        var provider = _CreateProvider(releaseObserver);
        var resource = Faker.Random.AlphaNumeric(10);
        await storage.TryAcquireReadAsync(
            $"distributed-lock:{resource}",
            "reader-1",
            TimeSpan.FromSeconds(10),
            AbortToken
        );

        using var cts = new CancellationTokenSource();
        releaseObserver.OnTryAcquireWriteCancellation = () => cts.Cancel();

        // when
        var act = async () =>
            await provider.TryAcquireWriteLockAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
                cts.Token
            );

        // then - OCE propagated AND cleanup ran
        await act.Should().ThrowAsync<OperationCanceledException>();
        releaseObserver.WriteReleaseCount.Should().Be(1);
    }

    [Fact]
    public async Task should_throw_when_monitoring_uses_infinite_lease()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var act = async () =>
            await provider.AcquireReadLockAsync(
                resource,
                new DistributedLockAcquireOptions
                {
                    TimeUntilExpires = Timeout.InfiniteTimeSpan,
                    Monitoring = LockMonitoringMode.Monitor,
                },
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class ReleaseObservingReaderWriterLockStorage(IDistributedReadWriteLockStorage inner)
        : IDistributedReadWriteLockStorage
    {
        public Action? OnTryAcquireWriteCancellation { get; set; }

        public int WriteReleaseCount { get; private set; }

        public ValueTask<bool> TryAcquireReadAsync(
            string resource,
            string leaseId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        )
        {
            return inner.TryAcquireReadAsync(resource, leaseId, ttl, cancellationToken);
        }

        public ValueTask<bool> TryExtendReadAsync(
            string resource,
            string leaseId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        )
        {
            return inner.TryExtendReadAsync(resource, leaseId, ttl, cancellationToken);
        }

        public ValueTask ReleaseReadAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        )
        {
            return inner.ReleaseReadAsync(resource, leaseId, cancellationToken);
        }

        public ValueTask<bool> TryAcquireWriteAsync(
            string resource,
            string leaseId,
            string waitingId,
            TimeSpan? ttl = null,
            TimeSpan? markerTtl = null,
            CancellationToken cancellationToken = default
        )
        {
            // Simulate cancellation observed mid-storage-call: trigger the caller CTS, then throw
            // OCE as Redis/StackExchange would when the token fires before the response lands.
            OnTryAcquireWriteCancellation?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return inner.TryAcquireWriteAsync(resource, leaseId, waitingId, ttl, markerTtl, cancellationToken);
        }

        public ValueTask<bool> TryExtendWriteAsync(
            string resource,
            string leaseId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        )
        {
            return inner.TryExtendWriteAsync(resource, leaseId, ttl, cancellationToken);
        }

        public ValueTask ReleaseWriteAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        )
        {
            WriteReleaseCount++;

            return inner.ReleaseWriteAsync(resource, leaseId, cancellationToken);
        }

        public ValueTask<bool> ValidateReadAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        )
        {
            return inner.ValidateReadAsync(resource, leaseId, cancellationToken);
        }

        public ValueTask<bool> ValidateWriteAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        )
        {
            return inner.ValidateWriteAsync(resource, leaseId, cancellationToken);
        }

        public ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
        {
            return inner.IsReadLockedAsync(resource, cancellationToken);
        }

        public ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
        {
            return inner.IsWriteLockedAsync(resource, cancellationToken);
        }

        public ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
        {
            return inner.GetReaderCountAsync(resource, cancellationToken);
        }
    }
}
