// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

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

    // EventId 24 == RegularLockLoggerExtensions.LogTryOnceSafetyDeadlineFired.
    private const int _SafetyDeadlineFiredEventId = 24;
    private const int _SafetyDeadlineSeconds = 10; // Mirrors _NonBlockingAcquireDeadline.

    [Fact]
    public async Task should_log_safety_deadline_eventid_and_tag_metric_stalled_when_deadline_fires()
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

        var logger = Substitute.For<ILogger<DistributedReadWriteLock>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var captured = new List<int>();
        logger
            .When(l =>
                l.Log(
                    Arg.Any<LogLevel>(),
                    Arg.Any<EventId>(),
                    Arg.Any<object>(),
                    Arg.Any<Exception?>(),
                    Arg.Any<Func<object, Exception?, string>>()
                )
            )
            .Do(call => captured.Add(call.Arg<EventId>().Id));

        var failedReasons = new List<string>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Headless.DistributedLocks" && instrument.Name == "headless.lock.failed")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<int>(
            (_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag is { Key: "reason", Value: string reason })
                    {
                        lock (failedReasons)
                        {
                            failedReasons.Add(reason);
                        }
                    }
                }
            }
        );
        meterListener.Start();

        _guidGenerator.Create().Returns(_ => Guid.NewGuid());
        var provider = new DistributedReadWriteLock(
            storage,
            outboxBus: null,
            new DistributedLockOptions(),
            _guidGenerator,
            _timeProvider,
            logger
        );
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var acquireTask = provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            CancellationToken.None
        );
        _timeProvider.Advance(TimeSpan.FromSeconds(_SafetyDeadlineSeconds + 1));
        await _DrainUntilAsync(() => acquireTask.IsCompleted);
        (await acquireTask).Should().BeNull();

        // then — log EventId proves the signal fires (isolated substitute logger); the metric
        // assertion is Contain-only because `headless.lock.failed` is process-wide and shared
        // with the regular lock.
        captured.Should().Contain(_SafetyDeadlineFiredEventId);
        failedReasons.Should().Contain(DistributedLockMetrics.ReasonStalled);
    }

    private static async Task<bool> _HangUntilCancelledAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        return false; // Unreachable — Task.Delay throws on cancellation.
    }

    private static async Task _DrainUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 2000 && !condition(); i++)
        {
            if (i % 100 == 0)
            {
                await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(1));
            }
            else
            {
                await Task.Yield();
            }
        }
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
        storage.WriteReleaseCount.Should().BeGreaterThan(0);
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
        ) => inner.TryAcquireReadAsync(resource, leaseId, ttl, cancellationToken);

        public ValueTask<bool> TryExtendReadAsync(
            string resource,
            string leaseId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        ) => inner.TryExtendReadAsync(resource, leaseId, ttl, cancellationToken);

        public ValueTask ReleaseReadAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        ) => inner.ReleaseReadAsync(resource, leaseId, cancellationToken);

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
        ) => inner.TryExtendWriteAsync(resource, leaseId, ttl, cancellationToken);

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
        ) => inner.ValidateReadAsync(resource, leaseId, cancellationToken);

        public ValueTask<bool> ValidateWriteAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        ) => inner.ValidateWriteAsync(resource, leaseId, cancellationToken);

        public ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default) =>
            inner.IsReadLockedAsync(resource, cancellationToken);

        public ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default) =>
            inner.IsWriteLockedAsync(resource, cancellationToken);

        public ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default) =>
            inner.GetReaderCountAsync(resource, cancellationToken);
    }
}
