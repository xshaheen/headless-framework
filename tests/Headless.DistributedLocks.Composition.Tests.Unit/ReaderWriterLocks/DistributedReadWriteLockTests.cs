// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
namespace Tests.ReaderWriterLocks;

public sealed class DistributedReadWriteLockTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IGuidGenerator _guidGenerator = Substitute.For<IGuidGenerator>();

    private DistributedReadWriteLock _CreateProvider(
        IDistributedReadWriteLockStorage? storage = null,
        IOutboxBus? outboxBus = null,
        DistributedLockOptions? options = null
    )
    {
        _guidGenerator.Create().Returns(_ => Guid.NewGuid());

        return new DistributedReadWriteLock(
            storage ?? new InMemoryDistributedReadWriteLockStorage(_timeProvider),
            outboxBus,
            options ?? new DistributedLockOptions(),
            _guidGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedReadWriteLock>()
        );
    }

    private DistributedReadWriteLock _CreateProviderWithLogger(
        IDistributedReadWriteLockStorage storage,
        ILogger<DistributedReadWriteLock> logger
    )
    {
        _guidGenerator.Create().Returns(_ => Guid.NewGuid());
        return new DistributedReadWriteLock(
            storage,
            outboxBus: null,
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

        var act = async () => await releaseTask;

        // then
        await act.Should().NotThrowAsync();
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
        var result = await acquireTask;

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
