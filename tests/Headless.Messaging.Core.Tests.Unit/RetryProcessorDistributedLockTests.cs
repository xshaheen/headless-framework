// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class RetryProcessorDistributedLockTests : IDisposable
{
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(30));
    private CancellationToken AbortToken => _cts.Token;
    private readonly IDistributedLock _realLockProvider;

    public RetryProcessorDistributedLockTests()
    {
        var storage = new InMemoryDistributedLockStorage(TimeProvider.System);
        _realLockProvider = new DistributedLock(
            storage,
            Substitute.For<IOutboxBus>(),
            new DistributedLockOptions(),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            NullLogger<DistributedLock>.Instance
        );
    }

    public void Dispose()
    {
        _cts.Dispose();
    }

    [Fact]
    public async Task should_skip_published_pickup_when_another_holder_owns_the_lock()
    {
        // given — pre-acquire published lock so the processor's try-once acquire returns null
        var externalLock = await _realLockProvider.TryAcquireAsync(
            MessagingKeys.PublishRetryResource("v1"),
            new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromMinutes(1) },
            AbortToken
        );
        externalLock.Should().NotBeNull("pre-condition: lock must be acquirable on empty store");
        await using var _ = externalLock!;

        var storage = Substitute.For<IDataStorage>();
        storage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));

        var processor = _CreateProcessor("v1", useStorageLock: true);
        using var context = _CreateContext(storage);

        // when
        await processor.ProcessAsync(context);
        await Task.Delay(200, AbortToken);

        // then — published path must not be reached because the lock was already held
        await storage.DidNotReceive().GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_skip_received_pickup_when_another_holder_owns_the_lock()
    {
        // given — pre-acquire received lock so the processor's try-once acquire returns null
        var externalLock = await _realLockProvider.TryAcquireAsync(
            MessagingKeys.ReceiveRetryResource("v1"),
            new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromMinutes(1) },
            AbortToken
        );
        externalLock.Should().NotBeNull("pre-condition: lock must be acquirable on empty store");
        await using var _ = externalLock!;

        var storage = Substitute.For<IDataStorage>();
        storage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));

        var processor = _CreateProcessor("v1", useStorageLock: true);
        using var context = _CreateContext(storage);

        // when
        await processor.ProcessAsync(context);
        await Task.Delay(200, AbortToken);

        // then — received path must not be reached because the lock was already held
        await storage.DidNotReceive().GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait(
        "ScenarioNote",
        "uses TrackingLockProvider because the real DisposableDistributedLock handle does not expose"
            + " RenewalCount, which this test must assert on; orthogonal to acquireTimeout semantics"
    )]
    public async Task should_not_manually_renew_received_retry_lock_when_consume_task_spans_polling_ticks()
    {
        // given — use TrackingLockProvider so we can inspect RenewalCount on the returned handle.
        // The processor should now rely on LockMonitoringMode.AutoExtend instead of cross-tick
        // RenewAsync calls against a cached received-retry handle.
        var lockAcquiredTcs = new TaskCompletionSource<TrackingLock>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var trackingProvider = new TrackingLockProvider("receive-retry");

        // Block the received storage query so the background consume task never completes.
        // The storage call also fires lockAcquiredTcs, proving the received task owns an acquired
        // lease while the second tick runs.
        var storageBlocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = Substitute.For<IDataStorage>();
        storage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));
        storage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                lockAcquiredTcs.TrySetResult(trackingProvider.LastIssuedReceiveRetryLock!);
                return new ValueTask<IEnumerable<MediumMessage>>(
                    storageBlocker.Task.ContinueWith(_ => (IEnumerable<MediumMessage>)[], TaskScheduler.Default)
                );
            });

        var processor = _CreateProcessor("v1", useStorageLock: true, lockProvider: trackingProvider);
        using var context = _CreateContext(storage);

        // when — tick 1: starts background consume task that acquires the lock and then blocks
        await processor.ProcessAsync(context);

        // Wait until the storage call fires — at that point the processor owns the received-retry
        // lease while the storage pickup remains in flight.
        var capturedLock = await lockAcquiredTcs.Task.WaitAsync(AbortToken);

        // when — tick 2: the in-progress guard waits without manually renewing the handle.
        await processor.ProcessAsync(context);

        // then
        capturedLock
            .RenewalCount.Should()
            .Be(0, "auto-extension belongs to the distributed-lock lease monitor, not ProcessAsync ticks");

        // Cleanup
        storageBlocker.TrySetResult();
        await Task.Delay(50, AbortToken);
    }

    [Fact]
    public async Task should_request_auto_extend_when_acquiring_retry_locks()
    {
        var captured = new ConcurrentBag<(string Resource, DistributedLockAcquireOptions Options)>();
        var fakeLock = Substitute.For<IDistributedLease>();
        var lockProvider = Substitute.For<IDistributedLock>();
        lockProvider
            .When(provider =>
            {
                _ = provider.TryAcquireAsync(
                    Arg.Any<string>(),
                    Arg.Any<DistributedLockAcquireOptions?>(),
                    Arg.Any<CancellationToken>()
                );
            })
            .Do(call =>
            {
                var options = call.ArgAt<DistributedLockAcquireOptions?>(1);
                if (options is not null)
                {
                    captured.Add((call.ArgAt<string>(0), options));
                }
            });
        lockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(fakeLock));

        var storage = Substitute.For<IDataStorage>();
        storage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));
        storage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));

        var processor = _CreateProcessor("v1", useStorageLock: true, lockProvider: lockProvider);
        using var context = _CreateContext(storage);

        await processor.ProcessAsync(context);

        await _EventuallyAsync(() =>
        {
            captured.Should().HaveCount(2);
            captured
                .Select(call => call.Resource)
                .Should()
                .BeEquivalentTo(MessagingKeys.PublishRetryResource("v1"), MessagingKeys.ReceiveRetryResource("v1"));
            captured
                .Should()
                .OnlyContain(call =>
                    call.Options.Monitoring == LockMonitoringMode.AutoExtend
                    && call.Options.AcquireTimeout == TimeSpan.Zero
                    && call.Options.TimeUntilExpires == processor.CurrentPollingInterval
                );
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task should_skip_retry_pickup_when_acquired_lease_is_already_lost()
    {
        var captured = new List<(LogLevel Level, int Id)>();
        var logger = _CreateCapturingLogger(captured);
        var lockProvider = Substitute.For<IDistributedLock>();
        lockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                Task.FromResult<IDistributedLease?>(new TrackingLock(canObserveLoss: true, initiallyLost: true))
            );

        var storage = Substitute.For<IDataStorage>();
        var processor = _CreateProcessor("v1", useStorageLock: true, lockProvider: lockProvider, logger: logger);
        using var context = _CreateContext(storage);

        await processor.ProcessAsync(context);

        await _EventuallyAsync(async () =>
        {
            await lockProvider
                .Received(2)
                .TryAcquireAsync(
                    Arg.Any<string>(),
                    Arg.Any<DistributedLockAcquireOptions?>(),
                    Arg.Any<CancellationToken>()
                );
            captured.Should().Contain(e => e.Level == LogLevel.Warning && e.Id == 79);
        });
        await storage.DidNotReceive().GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
        await storage.DidNotReceive().GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_log_lease_loss_without_cancelling_in_flight_retry_pickup()
    {
        var captured = new List<(LogLevel Level, int Id)>();
        var logger = _CreateCapturingLogger(captured);
        await using var publishedLease = new TrackingLock();
        await using var receivedLease = new TrackingLock(canObserveLoss: true);
        var lockProvider = Substitute.For<IDistributedLock>();
#pragma warning disable AsyncFixer04 // Substitute setup returns leases owned by this awaited test scope.
        lockProvider
            .TryAcquireAsync(
                MessagingKeys.PublishRetryResource("v1"),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(publishedLease));
        lockProvider
            .TryAcquireAsync(
                MessagingKeys.ReceiveRetryResource("v1"),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(receivedLease));
#pragma warning restore AsyncFixer04

        var receivedCallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storageBlocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = Substitute.For<IDataStorage>();
        storage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));
        storage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                receivedCallStarted.TrySetResult();
                return new ValueTask<IEnumerable<MediumMessage>>(
                    storageBlocker.Task.ContinueWith(_ => (IEnumerable<MediumMessage>)[], TaskScheduler.Default)
                );
            });

        var processor = _CreateProcessor("v1", useStorageLock: true, lockProvider: lockProvider, logger: logger);
        using var context = _CreateContext(storage);

        await processor.ProcessAsync(context);
        await receivedCallStarted.Task.WaitAsync(AbortToken);

        receivedLease.MarkLost();
        storageBlocker.TrySetResult();

        await _EventuallyAsync(async () =>
        {
            captured.Should().Contain(e => e.Level == LogLevel.Warning && e.Id == 79);
            await storage.Received(1).GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task should_call_storage_when_lock_always_granted()
    {
        // given — substitute that always hands back a non-null lock
        var fakeLock = Substitute.For<IDistributedLease>();
        var alwaysGranted = Substitute.For<IDistributedLock>();
        alwaysGranted
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(fakeLock));

        var storage = Substitute.For<IDataStorage>();
        storage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));
        storage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));

        var processor = _CreateProcessor("v1", useStorageLock: true, lockProvider: alwaysGranted);
        using var context = _CreateContext(storage);

        // when
        await processor.ProcessAsync(context);
        await Task.Delay(200, AbortToken);

        // then — both pickup paths must be exercised when locks are always granted
        await storage.Received().GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
        await storage.Received().GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_call_storage_without_acquiring_lock_when_use_storage_lock_is_false()
    {
        // given
        var mockProvider = Substitute.For<IDistributedLock>();

        var storage = Substitute.For<IDataStorage>();
        storage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));
        storage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));

        var processor = _CreateProcessor("v1", useStorageLock: false, lockProvider: mockProvider);
        using var context = _CreateContext(storage);

        // when
        await processor.ProcessAsync(context);
        await Task.Delay(200, AbortToken);

        // then — lock provider must never be called when UseStorageLock is false
        await mockProvider
            .DidNotReceive()
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            );
        await storage.Received().GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
        await storage.Received().GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_skip_published_pickup_when_previous_task_is_in_progress()
    {
        // given — published path is reached (always-granted lock), but the first storage call
        // is held open via a TaskCompletionSource so _publishedRetryConsumeTask never completes
        // before the second tick. The in-progress guard at IProcessor.NeedRetry.cs:172 must skip
        // spawning a new task while the previous one is still running under UseStorageLock.
        var fakeLock = Substitute.For<IDistributedLease>();
        var alwaysGranted = Substitute.For<IDistributedLock>();
        alwaysGranted
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(fakeLock));

        // The TCS fires from inside GetPublishedMessagesOfNeedRetryAsync so the test knows
        // exactly when _publishedRetryConsumeTask is in the running state. The published task
        // itself blocks until storageBlocker completes.
        var publishedCallFired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storageBlocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var storage = Substitute.For<IDataStorage>();
        storage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));
        storage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                publishedCallFired.TrySetResult();
                return new ValueTask<IEnumerable<MediumMessage>>(
                    storageBlocker.Task.ContinueWith(_ => (IEnumerable<MediumMessage>)[], TaskScheduler.Default)
                );
            });

        var processor = _CreateProcessor("v1", useStorageLock: true, lockProvider: alwaysGranted);
        using var context = _CreateContext(storage);

        try
        {
            // when — tick 1: spawns the published-retry task that blocks inside the storage call
            await processor.ProcessAsync(context);
            await publishedCallFired.Task.WaitAsync(AbortToken);

            // when — tick 2: in-progress guard must skip spawning a second published-retry task
            await processor.ProcessAsync(context);
            await Task.Delay(200, AbortToken);

            // then — exactly one storage pickup despite two ProcessAsync ticks
            await storage.Received(1).GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            // Cleanup — release the blocked task so the processor's background continuations
            // can complete and the test framework can dispose cleanly.
            storageBlocker.TrySetResult();
        }
    }

    [Fact]
    public async Task should_return_null_when_TryAcquireAsync_acquireTimeout_Zero_and_lock_already_held()
    {
        // given — hold the lock with the first acquire
        var firstLock = await _realLockProvider.TryAcquireAsync(
            "pin-test-resource",
            new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromMinutes(1) },
            AbortToken
        );
        firstLock.Should().NotBeNull("first acquire must succeed on an empty store");
        await using var _ = firstLock!;

        // when — try-once acquire with zero timeout while first is held
        var secondLock = await _realLockProvider.TryAcquireAsync(
            "pin-test-resource",
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromMinutes(1),
                AcquireTimeout = TimeSpan.Zero,
            },
            AbortToken
        );

        // then
        secondLock
            .Should()
            .BeNull("TimeSpan.Zero acquireTimeout must return null immediately when the lock is already held");

        if (secondLock is not null)
        {
            await secondLock.DisposeAsync();
        }
    }

    private MessageNeedToRetryProcessor _CreateProcessor(
        string version,
        bool useStorageLock,
        IDistributedLock? lockProvider = null,
        IDispatcher? dispatcher = null,
        ILogger<MessageNeedToRetryProcessor>? logger = null
    )
    {
        var messagingOptions = Options.Create(
            new MessagingOptions { Version = version, UseStorageLock = useStorageLock }
        );
        var retryOptions = Options.Create(
            new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1), AdaptivePolling = false }
        );
        return new MessageNeedToRetryProcessor(
            messagingOptions,
            retryOptions,
            logger ?? NullLogger<MessageNeedToRetryProcessor>.Instance,
            dispatcher ?? Substitute.For<IDispatcher>(),
            lockProvider ?? _realLockProvider
        );
    }

    private static ILogger<MessageNeedToRetryProcessor> _CreateCapturingLogger(List<(LogLevel Level, int Id)> captured)
    {
        var logger = Substitute.For<ILogger<MessageNeedToRetryProcessor>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
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
            .Do(ci => captured.Add((ci.Arg<LogLevel>(), ci.Arg<EventId>().Id)));

        return logger;
    }

    private ProcessingContext _CreateContext(IDataStorage storage)
    {
        var services = new ServiceCollection();
        services.AddSingleton(storage);
        var provider = services.BuildServiceProvider();
        return new ProcessingContext(provider, TimeProvider.System, AbortToken);
    }

    private async Task _EventuallyAsync(Func<Task> assertion)
    {
        var timeoutAt = TimeProvider.System.GetUtcNow() + TimeSpan.FromSeconds(2);
        Exception? lastFailure = null;

        while (TimeProvider.System.GetUtcNow() < timeoutAt)
        {
            try
            {
                await assertion();
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailure = ex;
                await Task.Delay(25, AbortToken);
            }
        }

        throw lastFailure ?? new TimeoutException("Timed out waiting for assertion to pass.");
    }

    private sealed class TrackingLockProvider(string resourceFilter) : IDistributedLock
    {
        public TimeSpan DefaultTimeUntilExpires => TimeSpan.FromMinutes(20);
        public TimeSpan DefaultAcquireTimeout => TimeSpan.FromSeconds(30);

        public async Task<IDistributedLease> AcquireAsync(
            string resource,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            return await TryAcquireAsync(resource, options, cancellationToken).ConfigureAwait(false)
                ?? throw new LockAcquisitionTimeoutException(resource);
        }

        /// <summary>The most recently issued lock that matched <c>resourceFilter</c>, if any.</summary>
        public TrackingLock? LastIssuedReceiveRetryLock { get; private set; }

        public Task<IDistributedLease?> TryAcquireAsync(
            string resource,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var trackingLock = new TrackingLock();
            if (resource.Contains(resourceFilter, StringComparison.Ordinal))
            {
                LastIssuedReceiveRetryLock = trackingLock;
            }
            return Task.FromResult<IDistributedLease?>(trackingLock);
        }

        public Task<bool> RenewAsync(
            string resource,
            string leaseId,
            TimeSpan? timeUntilExpires = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(false);

        public Task<string?> GetLeaseIdAsync(string resource, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default) =>
            Task.FromResult<TimeSpan?>(null);

        public Task<DistributedLockInfo?> GetLockInfoAsync(
            string resource,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<DistributedLockInfo?>(null);

        public Task<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<DistributedLockInfo>>([]);

        public Task<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);
    }

    private sealed class TrackingLock : IDistributedLease
    {
        private readonly CancellationTokenSource? _lostTokenSource;
        private int _renewalCount;

        public TrackingLock(bool canObserveLoss = false, bool initiallyLost = false)
        {
            if (canObserveLoss)
            {
                _lostTokenSource = new CancellationTokenSource();
                if (initiallyLost)
                {
                    _lostTokenSource.Cancel();
                }
            }
        }

        public string LeaseId => "tracking-lock-id";
        public long? FencingToken => null;
        public string Resource => "tracking-lock-resource";
        public int RenewalCount => Volatile.Read(ref _renewalCount);
        public DateTimeOffset DateAcquired => DateTimeOffset.UtcNow;
        public TimeSpan TimeWaitedForLock => TimeSpan.Zero;

        public CancellationToken LostToken => _lostTokenSource?.Token ?? CancellationToken.None;

        public bool CanObserveLoss => _lostTokenSource is not null;

        public void MarkLost() => _lostTokenSource?.Cancel();

        public Task ReleaseAsync() => Task.CompletedTask;

        public Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _renewalCount);
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            _lostTokenSource?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
