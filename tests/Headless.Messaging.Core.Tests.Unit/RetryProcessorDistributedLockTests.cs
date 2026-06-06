// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
            new SequentialAtEndGuidGenerator(),
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
        // Arrange — pre-acquire published lock so the processor's try-once acquire returns null
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

        var processor = _CreateProcessor("v1", storage, useStorageLock: true);
        using var context = _CreateContext(storage);

        // Act
        await processor.ProcessAsync(context);
        await Task.Delay(200, AbortToken);

        // Assert — published path must not be reached because the lock was already held
        await storage.DidNotReceive().GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_skip_received_pickup_when_another_holder_owns_the_lock()
    {
        // Arrange — pre-acquire received lock so the processor's try-once acquire returns null
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

        var processor = _CreateProcessor("v1", storage, useStorageLock: true);
        using var context = _CreateContext(storage);

        // Act
        await processor.ProcessAsync(context);
        await Task.Delay(200, AbortToken);

        // Assert — received path must not be reached because the lock was already held
        await storage.DidNotReceive().GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait(
        "ScenarioNote",
        "uses TrackingLockProvider because the real DisposableDistributedLock handle does not expose"
            + " RenewalCount, which this test must assert on; orthogonal to acquireTimeout semantics"
    )]
    public async Task should_renew_received_retry_lock_when_consume_task_spans_polling_ticks()
    {
        // Arrange — use TrackingLockProvider so we can inspect RenewalCount on the returned handle.
        // DisposableDistributedLock (the real handle type) does not expose RenewalCount, making
        // in-process tracking infeasible with the real provider for this specific assertion.
        //
        // The TCS is signalled from the storage call (which runs AFTER _receivedRetryHandle is
        // assigned by the processor), guaranteeing the renewal branch on tick 2 sees the handle.
        // Earlier, signaling from inside TryAcquireAsync fired before the processor's assignment,
        // which required a Task.Delay timing patch to mask the race.
        var lockAcquiredTcs = new TaskCompletionSource<TrackingLock>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var trackingProvider = new TrackingLockProvider("receive-retry");

        // Block the received storage query so the background consume task never completes.
        // The storage call also fires lockAcquiredTcs — this happens strictly after the processor
        // has stashed the lock into _receivedRetryHandle, so no timing patch is needed.
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

        var processor = _CreateProcessor("v1", storage, useStorageLock: true, lockProvider: trackingProvider);
        using var context = _CreateContext(storage);

        // Act — tick 1: starts background consume task that acquires the lock and then blocks
        await processor.ProcessAsync(context);

        // Wait until the storage call fires — at that point the processor has already executed
        // `_receivedRetryHandle = acquiredHandle;` between TryAcquireAsync and the storage call.
        var capturedLock = await lockAcquiredTcs.Task.WaitAsync(AbortToken);

        // Act — tick 2: renewal branch fires because _receivedRetryConsumeTask is still running
        await processor.ProcessAsync(context);

        // Assert
        capturedLock
            .RenewalCount.Should()
            .BeGreaterThanOrEqualTo(
                1,
                "the received-retry lock must be renewed when its consume task spans a polling tick"
            );

        // Cleanup
        storageBlocker.TrySetResult();
        await Task.Delay(50, AbortToken);
    }

    [Fact]
    public async Task should_call_storage_when_lock_always_granted()
    {
        // Arrange — substitute that always hands back a non-null lock
        var fakeLock = Substitute.For<IDistributedLease>();
        fakeLock.RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
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

        var processor = _CreateProcessor("v1", storage, useStorageLock: true, lockProvider: alwaysGranted);
        using var context = _CreateContext(storage);

        // Act
        await processor.ProcessAsync(context);
        await Task.Delay(200, AbortToken);

        // Assert — both pickup paths must be exercised when locks are always granted
        await storage.Received().GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
        await storage.Received().GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_call_storage_without_acquiring_lock_when_use_storage_lock_is_false()
    {
        // Arrange
        var mockProvider = Substitute.For<IDistributedLock>();

        var storage = Substitute.For<IDataStorage>();
        storage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));
        storage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IEnumerable<MediumMessage>>([]));

        var processor = _CreateProcessor("v1", storage, useStorageLock: false, lockProvider: mockProvider);
        using var context = _CreateContext(storage);

        // Act
        await processor.ProcessAsync(context);
        await Task.Delay(200, AbortToken);

        // Assert — lock provider must never be called when UseStorageLock is false
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
        // Arrange — published path is reached (always-granted lock), but the first storage call
        // is held open via a TaskCompletionSource so _publishedRetryConsumeTask never completes
        // before the second tick. The in-progress guard at IProcessor.NeedRetry.cs:172 must skip
        // spawning a new task while the previous one is still running under UseStorageLock.
        var fakeLock = Substitute.For<IDistributedLease>();
        fakeLock.RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
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

        var processor = _CreateProcessor("v1", storage, useStorageLock: true, lockProvider: alwaysGranted);
        using var context = _CreateContext(storage);

        try
        {
            // Act — tick 1: spawns the published-retry task that blocks inside the storage call
            await processor.ProcessAsync(context);
            await publishedCallFired.Task.WaitAsync(AbortToken);

            // Act — tick 2: in-progress guard must skip spawning a second published-retry task
            await processor.ProcessAsync(context);
            await Task.Delay(200, AbortToken);

            // Assert — exactly one storage pickup despite two ProcessAsync ticks
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
        // Arrange — hold the lock with the first acquire
        var firstLock = await _realLockProvider.TryAcquireAsync(
            "pin-test-resource",
            new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromMinutes(1) },
            AbortToken
        );
        firstLock.Should().NotBeNull("first acquire must succeed on an empty store");
        await using var _ = firstLock!;

        // Act — try-once acquire with zero timeout while first is held
        var secondLock = await _realLockProvider.TryAcquireAsync(
            "pin-test-resource",
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromMinutes(1),
                AcquireTimeout = TimeSpan.Zero,
            },
            AbortToken
        );

        // Assert
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
        IDataStorage storage,
        bool useStorageLock,
        IDistributedLock? lockProvider = null,
        IDispatcher? dispatcher = null
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
            NullLogger<MessageNeedToRetryProcessor>.Instance,
            dispatcher ?? Substitute.For<IDispatcher>(),
            lockProvider ?? _realLockProvider
        );
    }

    private ProcessingContext _CreateContext(IDataStorage storage)
    {
        var services = new ServiceCollection();
        services.AddSingleton(storage);
        var provider = services.BuildServiceProvider();
        return new ProcessingContext(provider, TimeProvider.System, AbortToken);
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
        private int _renewalCount;

        public string LeaseId => "tracking-lock-id";
        public long? FencingToken => null;
        public string Resource => "tracking-lock-resource";
        public int RenewalCount => Volatile.Read(ref _renewalCount);
        public DateTimeOffset DateAcquired => DateTimeOffset.UtcNow;
        public TimeSpan TimeWaitedForLock => TimeSpan.Zero;

        public CancellationToken LostToken => CancellationToken.None;

        public bool CanObserveLoss => false;

        public Task ReleaseAsync() => Task.CompletedTask;

        public Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _renewalCount);
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
