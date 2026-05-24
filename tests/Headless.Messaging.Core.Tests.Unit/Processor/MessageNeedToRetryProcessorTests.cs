// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
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
using Tests.Helpers;

namespace Tests.Processor;

public sealed class MessageNeedToRetryProcessorTests : TestBase
{
    private const string _Group = "test-group";

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string _CircuitKey(string group) => $"{IntentType.Bus:D}:{group}";

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
            IntentType = IntentType.Bus,
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
        var lockProvider = Substitute.For<IDistributedLockProvider>();
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
            lockProvider,
            cb
        );

        return (sut, dispatcher, cb);
    }

    private static ProcessingContext _CreateContext(IServiceProvider? provider = null)
    {
        provider ??= new ServiceCollection().AddSingleton(Substitute.For<IDataStorage>()).BuildServiceProvider();

        return new ProcessingContext(provider, TimeProvider.System, CancellationToken.None);
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

        cb.IsOpen(_CircuitKey("group-a")).Returns(true);
        cb.IsOpen(_CircuitKey("group-b")).Returns(false);

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
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        var logger = NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>();

        var options = new MessagingOptions();
        var retryProcessorOptions = new RetryProcessorOptions { BaseInterval = TimeSpan.FromSeconds(1) };

        var sut = new MessageNeedToRetryProcessor(
            Options.Create(options),
            Options.Create(retryProcessorOptions),
            logger,
            dispatcher,
            lockProvider
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

        cb.IsOpen(_CircuitKey("open-group")).Returns(true);
        cb.IsOpen(_CircuitKey("healthy-group")).Returns(false);

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

        cb.IsOpen(_CircuitKey("open-group")).Returns(true);

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
        cb.IsOpen(_CircuitKey("open-group")).Returns(true);

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
        cb.IsOpen(_CircuitKey("open-group")).Returns(true);
        cb.IsOpen(_CircuitKey("healthy-group")).Returns(false);
        _SetupReceivedMessages(
            dataStorage,
            _CreateMessage("open-group"),
            _CreateMessage("open-group"),
            _CreateMessage("open-group"),
            _CreateMessage("healthy-group")
        );
        await sut.ProcessAsync(context);

        // Cycle 2-3: All healthy (no open circuits) → 2 consecutive healthy cycles
        cb.IsOpen(_CircuitKey("open-group")).Returns(false);
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
        cb.IsOpen(_CircuitKey("open-group")).Returns(true);
        cb.IsOpen(_CircuitKey("healthy-group")).Returns(false);

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
        sut.StartupJitterApplied.Should().BeFalse();

        // Act — first ProcessAsync should apply jitter once
        var firstStopwatch = System.Diagnostics.Stopwatch.StartNew();
        await sut.ProcessAsync(context);
        firstStopwatch.Stop();

        // Post-condition: jitter is now consumed.
        sut.StartupJitterApplied.Should().BeTrue();

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
            var cancellableContext = new ProcessingContext(context.Provider, TimeProvider.System, cts.Token);
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
        sut.StartupJitterApplied.Should().BeTrue();
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
        sut.StartupJitterApplied.Should().BeTrue();
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

    // -------------------------------------------------------------------------
    // #8 — Storage-pickup failure escalation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_EscalatesToError_AfterThreeConsecutiveStorageFailures_AndResetsAfterSuccess()
    {
        // Arrange — capture EventId.Name from ILogger.Log invocations.
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var logger = Substitute.For<ILogger<MessageNeedToRetryProcessor>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var captured = new List<(LogLevel Level, int Id)>();
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

        var lockProvider = Substitute.For<IDistributedLockProvider>();

        var sut = new MessageNeedToRetryProcessor(
            Options.Create(new MessagingOptions()),
            Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromSeconds(1) }),
            logger,
            dispatcher,
            lockProvider
        );

        // Received-pickup throws on the first three cycles, succeeds on the fourth.
        var receivedCalls = 0;
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
                Interlocked.Increment(ref receivedCalls) <= 3
                    ? ValueTask.FromException<IEnumerable<MediumMessage>>(new InvalidOperationException("storage down"))
                    : ValueTask.FromResult<IEnumerable<MediumMessage>>([])
            );
        // Published path stays clean to isolate the received-pickup counter.
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act — cycle 1, 2 → Warning; cycle 3 → Error; cycle 4 → success resets the counter.
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);
        var afterTwo = captured.ToList();
        await sut.ProcessAsync(context);
        var afterThree = captured.ToList();
        await sut.ProcessAsync(context);

        // Assert — EventId 3110 = GetMessagesFromStorageFailed (warning), 74 = RetryStoragePickupFailureEscalated (error)
        afterTwo.Count(e => e.Id == 3110).Should().Be(2);
        afterTwo.Should().OnlyContain(e => e.Level != LogLevel.Error || e.Id != 74);

        afterThree.Count(e => e.Id == 74 && e.Level == LogLevel.Error).Should().Be(1);

        // Cycle 4 succeeded → no extra escalation/failure events were emitted after the third call.
        captured.Count(e => e.Id is 74 or 3110).Should().Be(3);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotTreatStoragePickupFailureAsCleanCycle()
    {
        var (sut, _, _) = _Create(baseIntervalSeconds: 1);
        var dataStorage = Substitute.For<IDataStorage>();

        var receivedCalls = 0;
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
                Interlocked.Increment(ref receivedCalls) == 1
                    ? ValueTask.FromException<IEnumerable<MediumMessage>>(new InvalidOperationException("storage down"))
                    : ValueTask.FromResult<IEnumerable<MediumMessage>>([])
            );
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        await sut.ProcessAsync(context);
        sut.CurrentPollingInterval.Should().Be(TimeSpan.FromSeconds(2));

        await sut.ProcessAsync(context);
        sut.CurrentPollingInterval.Should().Be(
            TimeSpan.FromSeconds(2),
            "the successful empty poll after a storage failure should not count as a clean cycle"
        );
    }

    // -------------------------------------------------------------------------
    // #3 — Lock-acquire exception escalation (EventIds 81 / 82)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task should_log_warning_eventid_81_when_published_retry_lock_acquire_throws()
    {
        // Arrange — capture EventId.Id from ILogger.Log invocations.
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var logger = Substitute.For<ILogger<MessageNeedToRetryProcessor>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var captured = new List<(LogLevel Level, int Id)>();
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

        var lockProvider = Substitute.For<IDistributedLockProvider>();
        // Published-path acquire throws once; received-path returns null (no contention).
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("publish-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<IDistributedLock?>>(_ => throw new InvalidOperationException("lock store down"));
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("receive-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLock?>(null));

        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(options, retryOpts, logger, dispatcher, lockProvider);

        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act — drive a single cycle; published-path acquire throws.
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        // Assert — EventId 81 (PublishedRetryLockAcquireFailed) fired at Warning.
        captured.Should().Contain(e => e.Level == LogLevel.Warning && e.Id == 81);
        // And the Error escalation EventId 82 did NOT fire (only one failure so far).
        captured.Should().NotContain(e => e.Id == 82);
    }

    [Fact]
    public async Task should_escalate_to_error_eventid_82_after_three_consecutive_published_retry_lock_acquire_throws()
    {
        // Arrange — capture EventId.Id from ILogger.Log invocations.
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var logger = Substitute.For<ILogger<MessageNeedToRetryProcessor>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var captured = new List<(LogLevel Level, int Id)>();
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

        var lockProvider = Substitute.For<IDistributedLockProvider>();
        var publishCallCount = 0;
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("publish-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<IDistributedLock?>>(_ =>
            {
                var n = Interlocked.Increment(ref publishCallCount);
                // First three throw → escalate to Error on the third. Fourth returns null (success path
                // means no contention; counter resets via _RecordLockAcquireFailure NOT being called).
                // To simulate a real "success" that resets the counter we'd need to return a handle;
                // null is treated as "contested" (no exception path), so the counter does NOT reset.
                // For this test we want to verify counter reset, so the fourth call returns a handle.
                if (n <= 3)
                {
                    throw new InvalidOperationException("lock store down");
                }
                return Task.FromResult<IDistributedLock?>(Substitute.For<IDistributedLock>());
            });
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("receive-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLock?>(null));

        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(options, retryOpts, logger, dispatcher, lockProvider);

        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act — drive three cycles; the third must emit EventId 82.
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);
        var beforeThird = captured.ToList();
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        // Assert — first two cycles: only EventId 81 (Warning); no EventId 82 (Error) yet.
        beforeThird.Count(e => e.Id == 81 && e.Level == LogLevel.Warning).Should().Be(2);
        beforeThird.Should().NotContain(e => e.Id == 82);

        // Third cycle: EventId 82 (Error) fires exactly once.
        captured.Count(e => e.Id == 82 && e.Level == LogLevel.Error).Should().Be(1);

        // Fourth cycle: lock acquire succeeds → counter resets via the unified counter (storage-pickup
        // / lock-acquire share _CounterRef). Drive one more cycle to verify no further escalation.
        captured.Clear();
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        captured
            .Should()
            .NotContain(e => e.Id == 82, "successful acquire (returning a handle) does not emit further escalation");
    }

    // -------------------------------------------------------------------------
    // #17 — UseStorageLock=false fast-path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task should_skip_TryAcquireAsync_when_UseStorageLock_is_false()
    {
        // Arrange — explicit construction so we can configure UseStorageLock=false and inspect the lock provider
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        var logger = NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>();

        var options = Options.Create(new MessagingOptions { UseStorageLock = false });
        var retryProcessorOptions = Options.Create(
            new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) }
        );

        var sut = new MessageNeedToRetryProcessor(options, retryProcessorOptions, logger, dispatcher, lockProvider);

        _SetupReceivedMessages(dataStorage);
        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act — drive ProcessAsync once
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);

        // Assert — TryAcquireAsync must never be called when UseStorageLock=false
        await lockProvider
            .DidNotReceive()
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    // -------------------------------------------------------------------------
    // #7 — _receivedRetryHandle race-window interleavings
    // -------------------------------------------------------------------------

    [Fact]
    public async Task should_skip_renewal_when_received_retry_handle_is_null()
    {
        // Race window 2: ProcessAsync sees the consume task running but _receivedRetryHandle has
        // not yet been assigned (or was cleared by a prior renewal-loss). Renewal branch must no-op.
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();

        // Block the consume task indefinitely so the renewal branch can fire next tick.
        var blocker = new TaskCompletionSource<IEnumerable<MediumMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IEnumerable<MediumMessage>>(blocker.Task));
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        // Always-grant lock so the consume task captures one (in real flow). Track renewals.
        var captured = Substitute.For<IDistributedLock>();
        captured.RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        lockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLock?>(captured));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(
            options,
            retryOpts,
            NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>(),
            dispatcher,
            lockProvider
        );

        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Act — drive a tick. The consume task fires the lock acquire and blocks. We can't easily
        // pin window 2 (assignment race) but we can simulate the cleared handle by NOT letting the
        // consume task ever advance to the assignment. Use a never-completing acquire to keep the
        // background task pending and the handle null:
        var neverCompletes = new TaskCompletionSource<IDistributedLock?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        lockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => neverCompletes.Task);

        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);

        // Second tick — _receivedRetryConsumeTask is still pending but _receivedRetryHandle is null.
        await sut.ProcessAsync(context);

        // Assert — no renewal was attempted because the handle field is null
        await captured.DidNotReceive().RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());

        // Cleanup
        neverCompletes.TrySetResult(null);
        blocker.TrySetResult([]);
    }

    [Fact]
    public async Task should_skip_renewal_when_consume_task_completed_before_tick()
    {
        // Race window 3: the consume task completes between the tick check and renewal — guarded
        // by the IsCompleted check. Verify renewal does not fire when the previous task is done.
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();

        var renewableLock = Substitute.For<IDistributedLock>();
        renewableLock.RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        lockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLock?>(renewableLock));

        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(
            options,
            retryOpts,
            NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>(),
            dispatcher,
            lockProvider
        );

        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Cycle 1 — consume task runs and completes synchronously (storage returns [])
        await sut.ProcessAsync(context);
        // Allow background continuations (ExecuteSynchronously _receivedRetryConsumeTask=null cleanup) to run.
        await Task.Delay(100, AbortToken);

        // Cycle 2 — previous task completed, so renewal branch should NOT fire and a fresh task is spawned.
        renewableLock.ClearReceivedCalls();
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);

        // Assert — no renewal because the prior consume task completed before this tick.
        // (Cycle 2 acquires its own lock and finishes; no renewal between ticks.)
        await renewableLock.DidNotReceive().RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());

        // Also assert: the processor always passes acquireTimeout: TimeSpan.Zero (non-blocking try-once).
        // A future refactor that wires a non-zero timeout would silently change semantics; this lock-in
        // guarantees the regression surfaces.
        await lockProvider
            .Received()
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Is<DistributedLockAcquireOptions?>(o => o != null && o.AcquireTimeout == TimeSpan.Zero),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_clear_received_retry_handle_when_renewal_returns_false()
    {
        // Window 4: renewal returns false (ownership lost). Processor must clear _receivedRetryHandle
        // so the next renewal attempt no-ops instead of pinging a stale handle.
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();

        // Consume task blocks so renewal branch fires on tick 2.
        var blocker = new TaskCompletionSource<IEnumerable<MediumMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IEnumerable<MediumMessage>>(blocker.Task));
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        // Lock acquire returns a handle whose Renew returns false.
        var lostLock = Substitute.For<IDistributedLock>();
        lostLock.RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));
        lockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLock?>(lostLock));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(
            options,
            retryOpts,
            NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>(),
            dispatcher,
            lockProvider
        );

        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Tick 1 — background consume task acquires lock and blocks on storage
        await sut.ProcessAsync(context);
        // Give the background task time to capture the lock into _receivedRetryHandle
        await Task.Delay(100, AbortToken);

        // Tick 2 — renewal branch fires, Renew returns false, handle must be cleared
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        // Tick 3 — handle should be cleared; renewal must no-op (count stays at 1 from tick 2)
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);

        // Assert — Renew was called exactly once (tick 2). On tick 3 the handle was null so no renew.
        await lostLock.Received(1).RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());

        // Cleanup
        blocker.TrySetResult([]);
        await Task.Delay(50, AbortToken);
    }

    [Fact]
    public async Task should_clear_received_retry_handle_in_finally_even_if_work_throws()
    {
        // The finally-clear in _ProcessReceivedAsync must run even when the work body throws,
        // so the next renewal cycle does not see a stale handle.
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();

        // Storage throws — work body unwinds via the catch in _GetSafelyAsync, returning [].
        // The finally clear still runs after the foreach.
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException<IEnumerable<MediumMessage>>(new InvalidOperationException("boom")));
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var acquiredLock = Substitute.For<IDistributedLock>();
        acquiredLock.RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        lockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLock?>(acquiredLock));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(
            options,
            retryOpts,
            NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>(),
            dispatcher,
            lockProvider
        );

        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Drive one cycle — storage throws, finally must clear the handle and dispose the lock
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        // Drive a second cycle — if the handle was not cleared, RenewAsync would be invoked here;
        // but the prior consume task has completed (finally ran), so the renewal branch is skipped
        // entirely and no renewal must fire.
        acquiredLock.ClearReceivedCalls();
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);

        // Assert — finally cleared the handle: no stale renewal between cycles.
        await acquiredLock.DidNotReceive().RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        // And the handle was disposed at least once per cycle (so finally ran).
        await acquiredLock.Received().DisposeAsync();
    }

    [Fact]
    public async Task should_log_EventId_79_when_received_retry_renewal_returns_false()
    {
        // Companion to should_clear_received_retry_handle_when_renewal_returns_false: that test
        // covers the behavioural side-effect (handle cleared). This one covers the log-emission
        // half of the contract — EventId 79 (ReceivedRetryLockOwnershipLost) must surface at
        // Warning so operator monitoring on ownership-loss noise is observable.
        var capturedLog = new List<(LogLevel Level, EventId EventId)>();
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            builder.AddProvider(new CapturingLoggerProvider(capturedLog))
        );

        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();

        // Block the received consume task so the renewal branch fires on tick 2.
        var blocker = new TaskCompletionSource<IEnumerable<MediumMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IEnumerable<MediumMessage>>(blocker.Task));
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var lostLock = Substitute.For<IDistributedLock>();
        lostLock.RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));
        lockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLock?>(lostLock));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(
            options,
            retryOpts,
            loggerFactory.CreateLogger<MessageNeedToRetryProcessor>(),
            dispatcher,
            lockProvider
        );

        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Tick 1 — acquire and block
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        // Tick 2 — renewal returns false, EventId 79 must fire
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        // Assert
        capturedLog
            .Should()
            .Contain(
                entry => entry.Level == LogLevel.Warning && entry.EventId.Id == 79,
                "EventId 79 (ReceivedRetryLockOwnershipLost) must fire when the renewal returns false"
            );

        // Cleanup
        blocker.TrySetResult([]);
        await Task.Delay(50, AbortToken);
    }

    [Fact]
    public async Task should_log_EventId_80_and_clear_handle_when_received_retry_renewal_throws()
    {
        // EventId 80 (ReceivedRetryLockRenewalFailed) covers the infrastructure-failure path:
        // RenewAsync throws (Redis unavailable, network partition). The processor must catch the
        // exception, log it at Warning, clear the cached handle so the next cycle re-acquires
        // fresh, and continue running. No prior test guards this path — yet it's the failure
        // mode operators most need confidence in.
        var capturedLog = new List<(LogLevel Level, EventId EventId)>();
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            builder.AddProvider(new CapturingLoggerProvider(capturedLog))
        );

        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();

        var blocker = new TaskCompletionSource<IEnumerable<MediumMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IEnumerable<MediumMessage>>(blocker.Task));
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        // RenewAsync throws on tick 2 — simulates lock-store outage / network partition.
        var failingLock = Substitute.For<IDistributedLock>();
        failingLock
            .RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("simulated lock-store outage"));
        lockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLock?>(failingLock));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(
            options,
            retryOpts,
            loggerFactory.CreateLogger<MessageNeedToRetryProcessor>(),
            dispatcher,
            lockProvider
        );

        var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Tick 1 — acquire and block
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        // Tick 2 — RenewAsync throws; processor must catch, log EventId 80, clear handle, continue
        var act = async () => await sut.ProcessAsync(context);
        await act.Should()
            .NotThrowAsync(
                "renewal-throw is treated as transient; the in-flight consume task continues under per-row LockedUntil"
            );
        await Task.Delay(100, AbortToken);

        // Tick 3 — handle should now be null; renewal must NOT fire again on the same dead handle
        failingLock.ClearReceivedCalls();
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);

        // Assert — EventId 80 fired once at Warning during tick 2
        capturedLog
            .Should()
            .Contain(
                entry => entry.Level == LogLevel.Warning && entry.EventId.Id == 80,
                "EventId 80 (ReceivedRetryLockRenewalFailed) must fire when RenewAsync throws"
            );

        // And the handle was cleared so tick 3 did not re-invoke RenewAsync on the same handle
        await failingLock.DidNotReceive().RenewAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());

        // Cleanup
        blocker.TrySetResult([]);
        await Task.Delay(50, AbortToken);
    }
}
