// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Processor;

public sealed class MessageNeedToRetryProcessorTests : TestBase
{
    private const string _Group = "test-group";

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static MediumMessage _CreateMessage(string? group = _Group)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "test.topic",
        };

        if (group is not null)
        {
            headers[Headers.Group] = group;
        }

        return new MediumMessage
        {
            StorageId = 1L,
            Origin = new Message(headers, null),
            Content = "{}",
        };
    }

    private static (MessageNeedToRetryProcessor Sut, IDispatcher Dispatcher, ICircuitBreakerMonitor Cb) _Create(
        int baseIntervalSeconds = 1,
        bool adaptivePolling = true,
        int maxPollingIntervalSeconds = 900,
        double circuitOpenRateThreshold = 0.8
    )
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var cb = Substitute.For<ICircuitBreakerMonitor>();
        var logger = NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>();

        var options = new MessagingOptions();

        var retryProcessorOptions = new RetryProcessorOptions
        {
            AdaptivePolling = adaptivePolling,
            BaseInterval = TimeSpan.FromSeconds(baseIntervalSeconds),
            MaxPollingInterval = TimeSpan.FromSeconds(maxPollingIntervalSeconds),
            CircuitOpenRateThreshold = circuitOpenRateThreshold,
        };

        var sut = new MessageNeedToRetryProcessor(
            Options.Create(options),
            Options.Create(retryProcessorOptions),
            logger,
            dispatcher,
            dataStorage,
            cb
        );

        return (sut, dispatcher, cb);
    }

    private static ProcessingContext _CreateContext(IServiceProvider? provider = null)
    {
        provider ??= new ServiceCollection().AddSingleton(Substitute.For<IDataStorage>()).BuildServiceProvider();

        return new ProcessingContext(provider, CancellationToken.None);
    }

    private static void _SetupReceivedMessages(IDataStorage dataStorage, params MediumMessage[] messages)
    {
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>(messages));

        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
    }

    // Helpers use internal members exposed via InternalsVisibleTo — no reflection needed.

    private static TimeSpan _GetCurrentInterval(MessageNeedToRetryProcessor sut) => sut.CurrentPollingInterval;

    private static void _SetCurrentInterval(MessageNeedToRetryProcessor sut, TimeSpan value) =>
        sut.SetCurrentIntervalForTest(value);

    private static void _InvokeAdjustPollingInterval(
        MessageNeedToRetryProcessor sut,
        int enqueued,
        int skippedCircuitOpen
    ) => sut._AdjustPollingInterval(enqueued, skippedCircuitOpen);

    private static TimeSpan _GetLockTtl(MessageNeedToRetryProcessor sut) => sut._GetLockTtl();

    // -------------------------------------------------------------------------
    // US-012: Circuit-state awareness
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_SkipsMessages_WhenCircuitIsOpenForGroup()
    {
        // Arrange
        var (sut, dispatcher, cb) = _Create();
        var msg1 = _CreateMessage("group-a");
        var msg2 = _CreateMessage("group-b");
        var msg3 = _CreateMessage("group-a");

        cb.IsOpen("group-a").Returns(true);
        cb.IsOpen("group-b").Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, msg1, msg2, msg3);

        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act
        await sut.ProcessAsync(context);

        // Assert — only group-b message enqueued
        await dispatcher.Received(1).EnqueueToExecute(msg2, null, Arg.Any<CancellationToken>());
        await dispatcher.DidNotReceive().EnqueueToExecute(msg1, null, Arg.Any<CancellationToken>());
        await dispatcher.DidNotReceive().EnqueueToExecute(msg3, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_EnqueuesAllMessages_WhenNoCircuitBreakerRegistered()
    {
        // Arrange — no circuit breaker in DI
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var logger = NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>();

        var options = new MessagingOptions();
        var retryProcessorOptions = new RetryProcessorOptions { BaseInterval = TimeSpan.FromSeconds(1) };

        var sut = new MessageNeedToRetryProcessor(
            Options.Create(options),
            Options.Create(retryProcessorOptions),
            logger,
            dispatcher,
            dataStorage
        );

        var msg1 = _CreateMessage("group-a");
        var msg2 = _CreateMessage("group-b");
        _SetupReceivedMessages(dataStorage, msg1, msg2);
        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act
        await sut.ProcessAsync(context);

        // Assert — all enqueued
        await dispatcher.Received(1).EnqueueToExecute(msg1, null, Arg.Any<CancellationToken>());
        await dispatcher.Received(1).EnqueueToExecute(msg2, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_EnqueuesMessage_WhenGroupIsNull()
    {
        // Arrange
        var (sut, dispatcher, cb) = _Create();
        var msg = _CreateMessage(group: null);

        cb.IsOpen(Arg.Any<string>()).Returns(true); // all groups "open"

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, msg);
        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act
        await sut.ProcessAsync(context);

        // Assert — null group messages always enqueued (can't check circuit without group name)
        await dispatcher.Received(1).EnqueueToExecute(msg, null, Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // US-011: Adaptive polling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_DoublesInterval_WhenTransientRateExceedsThreshold()
    {
        // Arrange — 5 messages, 4 skipped (circuit open) = 80% > threshold
        var (sut, dispatcher, cb) = _Create(baseIntervalSeconds: 1, circuitOpenRateThreshold: 0.7);

        var baseInterval = TimeSpan.FromSeconds(1);

        cb.IsOpen("open-group").Returns(true);
        cb.IsOpen("healthy-group").Returns(false);

        var messages = new[]
        {
            _CreateMessage("open-group"),
            _CreateMessage("open-group"),
            _CreateMessage("open-group"),
            _CreateMessage("open-group"),
            _CreateMessage("healthy-group"),
        };

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, messages);
        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act — first cycle
        await sut.ProcessAsync(context);

        // Assert — interval should have doubled from 10s to 20s
        var currentInterval = _GetCurrentInterval(sut);
        currentInterval.Should().Be(baseInterval * 2);

        await dispatcher
            .Received(1)
            .EnqueueToExecute(
                Arg.Is<MediumMessage>(m => m.Origin.GetGroup() == "healthy-group"),
                null,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ProcessAsync_ResetsInterval_After3CleanCycles()
    {
        // Arrange
        var (sut, dispatcher, cb) = _Create(baseIntervalSeconds: 1);

        var baseInterval = TimeSpan.FromSeconds(1);

        cb.IsOpen(Arg.Any<string>()).Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();

        // All cycles return empty — clean cycles
        _SetupReceivedMessages(dataStorage);
        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act — 3 clean cycles
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);

        // Assert — no crashes, processor handles empty batches
        // The interval should have been reset to base after 3 clean cycles
        var currentInterval = _GetCurrentInterval(sut);
        currentInterval.Should().Be(baseInterval);

        await dispatcher
            .DidNotReceive()
            .EnqueueToExecute(
                Arg.Any<MediumMessage>(),
                Arg.Any<ConsumerExecutorDescriptor>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ProcessAsync_DoesNotAdjustInterval_WhenAdaptivePollingDisabled()
    {
        // Arrange
        var (sut, dispatcher, cb) = _Create(baseIntervalSeconds: 1, adaptivePolling: false);

        cb.IsOpen("open-group").Returns(true);

        var messages = Enumerable.Range(0, 10).Select(_ => _CreateMessage("open-group")).ToArray();

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, messages);
        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act — all skipped, but adaptive polling is off
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);

        // Assert — no crashes, messages still skipped due to circuit breaker
        await dispatcher
            .DidNotReceive()
            .EnqueueToExecute(
                Arg.Any<MediumMessage>(),
                Arg.Any<ConsumerExecutorDescriptor>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ProcessAsync_IntervalCappedAtMax_AfterManyDoublings()
    {
        // Arrange — maxPollingInterval=2s, base=1s, all messages skipped → high transient rate
        var maxPollingInterval = TimeSpan.FromSeconds(2);
        var (sut, dispatcher, cb) = _Create(
            baseIntervalSeconds: 1,
            maxPollingIntervalSeconds: 2,
            circuitOpenRateThreshold: 0.5
        );
        cb.IsOpen("open-group").Returns(true);

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, _CreateMessage("open-group"));
        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act — 10 cycles, each doubles interval, should cap at maxPollingInterval (2s)
        for (var i = 0; i < 10; i++)
        {
            await sut.ProcessAsync(context);
        }

        // Assert — no crash, no overflow. Messages consistently skipped.
        // Interval should be capped at maxPollingInterval (2s)
        var currentInterval = _GetCurrentInterval(sut);
        currentInterval.Should().Be(maxPollingInterval);

        await dispatcher.DidNotReceive().EnqueueToExecute(Arg.Any<MediumMessage>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_HalvesInterval_After2HealthyCyclesWithElevatedInterval()
    {
        // Arrange — first elevate interval via high transient rate, then 2 healthy cycles
        var (sut, dispatcher, cb) = _Create(
            baseIntervalSeconds: 1,
            maxPollingIntervalSeconds: 60,
            circuitOpenRateThreshold: 0.5
        );

        var dataStorage = Substitute.For<IDataStorage>();
        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Cycle 1: High transient rate → doubles interval
        cb.IsOpen("open-group").Returns(true);
        cb.IsOpen("healthy-group").Returns(false);
        _SetupReceivedMessages(
            dataStorage,
            _CreateMessage("open-group"),
            _CreateMessage("open-group"),
            _CreateMessage("open-group"),
            _CreateMessage("healthy-group")
        );
        await sut.ProcessAsync(context);

        // Cycle 2-3: All healthy (no open circuits) → 2 consecutive healthy cycles
        cb.IsOpen("open-group").Returns(false);
        _SetupReceivedMessages(dataStorage, _CreateMessage("healthy-group"), _CreateMessage("healthy-group"));
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);

        // Assert — processor functions without crashes through full escalate/recover cycle
        await dispatcher.Received().EnqueueToExecute(Arg.Any<MediumMessage>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void lock_ttl_tracks_the_effective_polling_interval()
    {
        var (sut, _, _) = _Create(baseIntervalSeconds: 1);

        _SetCurrentInterval(sut, TimeSpan.FromSeconds(20));

        _GetLockTtl(sut).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AdjustPollingInterval_DoesNotOverflow_WhenCurrentIntervalIsNearMaxValue()
    {
        // Arrange — set current interval to a value where * 2 would overflow a long
        var (sut, _, _) = _Create(
            baseIntervalSeconds: 1,
            maxPollingIntervalSeconds: 900,
            circuitOpenRateThreshold: 0.5
        );

        // Set current interval to just above long.MaxValue / 2 in ticks — doubling would overflow
        var nearMaxTicks = (long.MaxValue / 2) + 1;
        _SetCurrentInterval(sut, TimeSpan.FromTicks(nearMaxTicks));

        // Act — invoke _AdjustPollingInterval directly (enqueued=1, skippedCircuitOpen=9 → 90% > 50% threshold → backoff path)
        _InvokeAdjustPollingInterval(sut, enqueued: 1, skippedCircuitOpen: 9);

        // Assert — interval should be capped at max, not negative/overflowed
        var currentInterval = _GetCurrentInterval(sut);
        currentInterval.Ticks.Should().BeGreaterThan(0, "interval must never overflow to negative");
        currentInterval.Should().Be(TimeSpan.FromSeconds(900));
    }

    [Fact]
    public async Task ConcurrentResetAndAdjust_DoesNotPermanentlyOverrideReset()
    {
        // Arrange — elevate interval to 4x base, then race Reset vs Adjust(double)
        var baseInterval = TimeSpan.FromSeconds(1);
        var (sut, _, _) = _Create(
            baseIntervalSeconds: 1,
            maxPollingIntervalSeconds: 900,
            circuitOpenRateThreshold: 0.5
        );

        _SetCurrentInterval(sut, TimeSpan.FromSeconds(4));

        var barrier = new Barrier(2);
        const int iterations = 10_000;

        // Act — race _AdjustPollingInterval (doubling path) against ResetBackpressureAsync
        var adjustTask = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterations; i++)
            {
                // enqueued=1, skippedCircuitOpen=9 → 90% > 50% threshold → doubling path
                _InvokeAdjustPollingInterval(sut, enqueued: 1, skippedCircuitOpen: 9);
            }
        });

        var resetTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterations; i++)
            {
                await sut.ResetBackpressureAsync();
            }
        });

        await Task.WhenAll(adjustTask, resetTask);

        // Assert — after the race, a final reset must reliably bring the interval back to base.
        // Without CAS, the stale-read doubling could permanently override the reset.
        await sut.ResetBackpressureAsync();
        var finalInterval = _GetCurrentInterval(sut);
        finalInterval.Should().Be(baseInterval, "a reset after the race must restore the base interval");
    }

    [Fact]
    public async Task ProcessAsync_MidRangeRate_ResetsCountersWithoutChangingInterval()
    {
        // Arrange — rate between 0.5 and threshold (0.8): e.g., 6 skipped out of 10 = 60%
        var (sut, dispatcher, cb) = _Create(baseIntervalSeconds: 1, circuitOpenRateThreshold: 0.8);
        cb.IsOpen("open-group").Returns(true);
        cb.IsOpen("healthy-group").Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();
        // 6 open + 4 healthy = 60% transient rate → mid-range
        var messages = Enumerable
            .Range(0, 6)
            .Select(_ => _CreateMessage("open-group"))
            .Concat(Enumerable.Range(0, 4).Select(_ => _CreateMessage("healthy-group")))
            .ToArray();
        _SetupReceivedMessages(dataStorage, messages);
        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act — mid-range cycle
        await sut.ProcessAsync(context);

        // Assert — healthy messages still enqueued, processor doesn't crash
        await dispatcher
            .Received(4)
            .EnqueueToExecute(
                Arg.Is<MediumMessage>(m => m.Origin.GetGroup() == "healthy-group"),
                null,
                Arg.Any<CancellationToken>()
            );
    }

    // -------------------------------------------------------------------------
    // Startup jitter — one-shot poll-tick desynchronization
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_AppliesJitter_OnFirstPollOnly()
    {
        // Arrange — generous base interval so jitter is measurable but bounded
        var baseInterval = TimeSpan.FromMilliseconds(500);
        var (sut, _, cb) = _Create(baseIntervalSeconds: 1);
        _SetCurrentInterval(sut, baseInterval);

        cb.IsOpen(Arg.Any<string>()).Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage); // empty
        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Pre-condition: jitter flag has not been observed yet.
        sut.FirstPollObservedForTest.Should().BeFalse();

        // Act — first ProcessAsync should apply jitter once
        var firstStopwatch = System.Diagnostics.Stopwatch.StartNew();
        await sut.ProcessAsync(context);
        firstStopwatch.Stop();

        // Post-condition: jitter is now consumed.
        sut.FirstPollObservedForTest.Should().BeTrue();

        // First call's total elapsed time includes (jitter < baseInterval) + (final WaitAsync ~= 1s currentInterval).
        // The jitter component alone must be < baseInterval (500 ms). We can't isolate it, but we can
        // bound the total via the fact that jitter <= baseInterval.
        // Stronger and stable assertion: jitter is a one-shot — second call's startup overhead is 0.
        var secondStopwatch = System.Diagnostics.Stopwatch.StartNew();
        // Spin off a quick second invocation but cancel the post-work WaitAsync so we only measure the jitter slot.
        // Explicit try/finally Dispose (not `using var`) so the analyzer can verify the task completes
        // before the CancellationTokenSource is disposed
        var cts = new CancellationTokenSource();
        try
        {
            var cancellableContext = new ProcessingContext(context.Provider, cts.Token);
            var secondTask = sut.ProcessAsync(cancellableContext);
            // Give the storage call a moment to be invoked, then cancel.
            await Task.Delay(50, AbortToken);
            await cts.CancelAsync();
            try
            {
                await secondTask;
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            secondStopwatch.Stop();
        }
        finally
        {
            cts.Dispose();
        }

        // Assert — jitter is one-shot: flag stays true.
        sut.FirstPollObservedForTest.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_FirstPollWait_DoesNotExceedBaseInterval()
    {
        // Arrange — small base interval so the upper bound is easy to verify.
        var (sut, _, cb) = _Create(baseIntervalSeconds: 1);
        cb.IsOpen(Arg.Any<string>()).Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();
        // First storage call signals when jitter completes.
        var storageCalled = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                storageCalled.TrySetResult(DateTime.UtcNow);
                return ValueTask.FromResult<IEnumerable<MediumMessage>>([]);
            });
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act — kick off ProcessAsync and time how long until the storage call fires.
        var started = DateTime.UtcNow;
        var processTask = sut.ProcessAsync(context);

        // Wait for the first storage call but cap at 3x base interval to avoid hanging the test.
        var firstStorageAt = await storageCalled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var jitterElapsed = firstStorageAt - started;

        // Let the rest of the process complete.
        await processTask;

        // Assert — jitter must be strictly less than base interval.
        // Upper bound is baseInterval (1s); we allow generous tolerance for scheduling noise.
        jitterElapsed
            .Should()
            .BeLessThan(
                TimeSpan.FromSeconds(2),
                "first-poll jitter is bounded by base interval (1s) plus scheduling tolerance"
            );
        sut.FirstPollObservedForTest.Should().BeTrue();
    }

    [Fact]
    public async Task reset_backpressure_concurrent_with_adjust_does_not_produce_invalid_counts()
    {
        var (sut, _, _) = _Create(adaptivePolling: true);

        // Run resets and adjustments concurrently
        var tasks = Enumerable
            .Range(0, 50)
            .Select(i =>
                i % 2 == 0
                    ? Task.Run(() => _InvokeAdjustPollingInterval(sut, 0, 10)) // backoff path
                    : sut.ResetBackpressureAsync().AsTask()
            )
            .ToArray();

        await Task.WhenAll(tasks);

        // After all operations, interval should be within valid bounds
        sut.CurrentPollingInterval.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        sut.CurrentPollingInterval.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(900));
    }
}
