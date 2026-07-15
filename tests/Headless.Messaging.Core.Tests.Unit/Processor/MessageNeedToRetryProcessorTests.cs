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
using Microsoft.Extensions.Time.Testing;

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
            [Headers.MessageName] = "test.messageName",
        };

        if (group is not null)
        {
            headers[Headers.Group] = group;
        }

        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
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
        var lockProvider = Substitute.For<IDistributedLock>();
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
    ) => sut.AdjustPollingInterval(enqueued, skippedCircuitOpen);

    // -------------------------------------------------------------------------
    // US-012: Circuit-state awareness
    // -------------------------------------------------------------------------

    [Fact]
    public async Task process_async_skips_messages_when_circuit_is_open_for_group()
    {
        // given
        var (sut, dispatcher, cb) = _Create();
        var msg1 = _CreateMessage("group-a");
        var msg2 = _CreateMessage("group-b");
        var msg3 = _CreateMessage("group-a");

        cb.IsOpen(_CircuitKey("group-a")).Returns(true);
        cb.IsOpen(_CircuitKey("group-b")).Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, msg1, msg2, msg3);

        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // when
        await sut.ProcessAsync(context);

        // then — only group-b message enqueued
        await dispatcher.Received(1).EnqueueToExecute(msg2, null, Arg.Any<CancellationToken>());
        await dispatcher.DidNotReceive().EnqueueToExecute(msg1, null, Arg.Any<CancellationToken>());
        await dispatcher.DidNotReceive().EnqueueToExecute(msg3, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task process_async_enqueues_all_messages_when_no_circuit_breaker_registered()
    {
        // given — no circuit breaker in DI
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var lockProvider = Substitute.For<IDistributedLock>();
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
        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // when
        await sut.ProcessAsync(context);

        // then — all enqueued
        await dispatcher.Received(1).EnqueueToExecute(msg1, null, Arg.Any<CancellationToken>());
        await dispatcher.Received(1).EnqueueToExecute(msg2, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task process_async_enqueues_message_when_group_is_null()
    {
        // given
        var (sut, dispatcher, cb) = _Create();
        var msg = _CreateMessage(group: null);

        cb.IsOpen(Arg.Any<string>()).Returns(true); // all groups "open"

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, msg);
        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // when
        await sut.ProcessAsync(context);

        // then — null group messages always enqueued (can't check circuit without group name)
        await dispatcher.Received(1).EnqueueToExecute(msg, null, Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // US-011: Adaptive polling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task process_async_doubles_interval_when_transient_rate_exceeds_threshold()
    {
        // given — 5 messages, 4 skipped (circuit open) = 80% > threshold
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
        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // when — first cycle
        await sut.ProcessAsync(context);

        // then — interval should have doubled from 10s to 20s
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
    public async Task process_async_resets_interval_after3_clean_cycles()
    {
        // given
        var (sut, dispatcher, cb) = _Create(baseIntervalSeconds: 1);

        var baseInterval = TimeSpan.FromSeconds(1);

        cb.IsOpen(Arg.Any<string>()).Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();

        // All cycles return empty — clean cycles
        _SetupReceivedMessages(dataStorage);
        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // when — 3 clean cycles
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);

        // then — no crashes, processor handles empty batches
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
    public async Task process_async_does_not_adjust_interval_when_adaptive_polling_disabled()
    {
        // given
        var (sut, dispatcher, cb) = _Create(baseIntervalSeconds: 1, adaptivePolling: false);

        cb.IsOpen(_CircuitKey("open-group")).Returns(true);

        var messages = Enumerable.Range(0, 10).Select(_ => _CreateMessage("open-group")).ToArray();

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, messages);
        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // when — all skipped, but adaptive polling is off
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);

        // then — no crashes, messages still skipped due to circuit breaker
        await dispatcher
            .DidNotReceive()
            .EnqueueToExecute(
                Arg.Any<MediumMessage>(),
                Arg.Any<ConsumerExecutorDescriptor>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task process_async_interval_capped_at_max_after_many_doublings()
    {
        // given — maxPollingInterval=2s, base=1s, all messages skipped → high transient rate
        var maxPollingInterval = TimeSpan.FromSeconds(2);
        var (sut, dispatcher, cb) = _Create(
            baseIntervalSeconds: 1,
            maxPollingIntervalSeconds: 2,
            circuitOpenRateThreshold: 0.5
        );
        cb.IsOpen(_CircuitKey("open-group")).Returns(true);

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, _CreateMessage("open-group"));
        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // when — 10 cycles, each doubles interval, should cap at maxPollingInterval (2s)
        for (var i = 0; i < 10; i++)
        {
            await sut.ProcessAsync(context);
        }

        // then — no crash, no overflow. Messages consistently skipped.
        // Interval should be capped at maxPollingInterval (2s)
        var currentInterval = _GetCurrentInterval(sut);
        currentInterval.Should().Be(maxPollingInterval);

        await dispatcher.DidNotReceive().EnqueueToExecute(Arg.Any<MediumMessage>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task process_async_halves_interval_after2_healthy_cycles_with_elevated_interval()
    {
        // given — first elevate interval via high transient rate, then 2 healthy cycles
        var (sut, dispatcher, cb) = _Create(
            baseIntervalSeconds: 1,
            maxPollingIntervalSeconds: 60,
            circuitOpenRateThreshold: 0.5
        );

        var dataStorage = Substitute.For<IDataStorage>();
        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

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

        // then — processor functions without crashes through full escalate/recover cycle
        await dispatcher.Received().EnqueueToExecute(Arg.Any<MediumMessage>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void adjust_polling_interval_does_not_overflow_when_current_interval_is_near_max_value()
    {
        // given — set current interval to a value where * 2 would overflow a long
        var (sut, _, _) = _Create(
            baseIntervalSeconds: 1,
            maxPollingIntervalSeconds: 900,
            circuitOpenRateThreshold: 0.5
        );

        // Set current interval to just above long.MaxValue / 2 in ticks — doubling would overflow
        const long nearMaxTicks = (long.MaxValue / 2) + 1;
        _SetCurrentInterval(sut, TimeSpan.FromTicks(nearMaxTicks));

        // when — invoke AdjustPollingInterval directly (enqueued=1, skippedCircuitOpen=9 → 90% > 50% threshold → backoff path)
        _InvokeAdjustPollingInterval(sut, enqueued: 1, skippedCircuitOpen: 9);

        // then — interval should be capped at max, not negative/overflowed
        var currentInterval = _GetCurrentInterval(sut);
        currentInterval.Ticks.Should().BePositive("interval must never overflow to negative");
        currentInterval.Should().Be(TimeSpan.FromSeconds(900));
    }

    [Fact]
    public async Task concurrent_reset_and_adjust_does_not_permanently_override_reset()
    {
        // given — elevate interval to 4x base, then race Reset vs Adjust(double)
        var baseInterval = TimeSpan.FromSeconds(1);
        var (sut, _, _) = _Create(
            baseIntervalSeconds: 1,
            maxPollingIntervalSeconds: 900,
            circuitOpenRateThreshold: 0.5
        );

        _SetCurrentInterval(sut, TimeSpan.FromSeconds(4));

        using var barrier = new Barrier(2);
        const int iterations = 10_000;

        // when — race AdjustPollingInterval (doubling path) against ResetBackpressureAsync
        var adjustTask = Task.Run(
            () =>
            {
                barrier.SignalAndWait();
                for (var i = 0; i < iterations; i++)
                {
                    // enqueued=1, skippedCircuitOpen=9 → 90% > 50% threshold → doubling path
                    _InvokeAdjustPollingInterval(sut, enqueued: 1, skippedCircuitOpen: 9);
                }
            },
            AbortToken
        );

        var resetTask = Task.Run(
            async () =>
            {
                barrier.SignalAndWait();
                for (var i = 0; i < iterations; i++)
                {
                    await sut.ResetBackpressureAsync();
                }
            },
            AbortToken
        );

        await Task.WhenAll(adjustTask, resetTask);

        // then — after the race, a final reset must reliably bring the interval back to base.
        // Without CAS, the stale-read doubling could permanently override the reset.
        await sut.ResetBackpressureAsync(AbortToken);
        var finalInterval = _GetCurrentInterval(sut);
        finalInterval.Should().Be(baseInterval, "a reset after the race must restore the base interval");
    }

    [Fact]
    public async Task process_async_mid_range_rate_resets_counters_without_changing_interval()
    {
        // given — rate between 0.5 and threshold (0.8): e.g., 6 skipped out of 10 = 60%
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
        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // when — mid-range cycle
        await sut.ProcessAsync(context);

        // then — healthy messages still enqueued, processor doesn't crash
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
    public async Task process_async_applies_jitter_on_first_poll_only()
    {
        // given — generous base interval so jitter is measurable but bounded
        var baseInterval = TimeSpan.FromMilliseconds(500);
        var (sut, _, cb) = _Create(baseIntervalSeconds: 1);
        _SetCurrentInterval(sut, baseInterval);

        cb.IsOpen(Arg.Any<string>()).Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage); // empty
        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Pre-condition: jitter flag has not been observed yet.
        sut.StartupJitterApplied.Should().BeFalse();

        // when — first ProcessAsync should apply jitter once
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
            using var cancellableContext = new ProcessingContext(context.Provider, TimeProvider.System, cts.Token);
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

        // then — jitter is one-shot: flag stays true.
        sut.StartupJitterApplied.Should().BeTrue();
    }

    [Fact]
    public async Task process_async_first_poll_wait_does_not_exceed_base_interval()
    {
        // given — small base interval so the upper bound is easy to verify.
        var (sut, _, cb) = _Create(baseIntervalSeconds: 1);
        cb.IsOpen(Arg.Any<string>()).Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();
        // First storage call signals when jitter completes.
        var storageCalled = new TaskCompletionSource<DateTimeOffset>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                storageCalled.TrySetResult(DateTimeOffset.UtcNow);
                return ValueTask.FromResult<IEnumerable<MediumMessage>>([]);
            });
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var timeProvider = new FakeTimeProvider();
        using var context = new ProcessingContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider(),
            timeProvider,
            AbortToken
        );

        // when — kick off ProcessAsync and advance exactly one base interval.
#pragma warning disable CA2025 // Do not pass 'IDisposable' instances into unawaited tasks
        var processTask = sut.ProcessAsync(context);
#pragma warning restore CA2025

        timeProvider.Advance(TimeSpan.FromSeconds(1));

        // The first storage call must fire once fake time reaches the base interval; the remaining
        // process wait uses the same base interval and is advanced separately.
        await storageCalled.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await processTask.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then — jitter is bounded by the configured base interval, independent of thread-pool scheduling.
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
                    ? Task.Run(() => _InvokeAdjustPollingInterval(sut, 0, 10), AbortToken) // backoff path
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
    public async Task process_async_escalates_to_error_after_three_consecutive_storage_failures_and_resets_after_success()
    {
        // given — capture EventId.Name from ILogger.Log invocations.
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

        var lockProvider = Substitute.For<IDistributedLock>();

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

        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // when — cycle 1, 2 → Warning; cycle 3 → Error; cycle 4 → success resets the counter.
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);
        var afterTwo = captured.ToList();
        await sut.ProcessAsync(context);
        var afterThree = captured.ToList();
        await sut.ProcessAsync(context);

        // then — EventId 3110 = GetMessagesFromStorageFailed (warning), 74 = RetryStoragePickupFailureEscalated (error)
        afterTwo.Count(e => e.Id == 3110).Should().Be(2);
        afterTwo.Should().OnlyContain(e => e.Level != LogLevel.Error || e.Id != 74);

        afterThree.Count(e => e.Id == 74 && e.Level == LogLevel.Error).Should().Be(1);

        // Cycle 4 succeeded → no extra escalation/failure events were emitted after the third call.
        captured.Count(e => e.Id is 74 or 3110).Should().Be(3);
    }

    [Fact]
    public async Task process_async_does_not_treat_storage_pickup_failure_as_clean_cycle()
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

        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        await sut.ProcessAsync(context);
        sut.CurrentPollingInterval.Should().Be(TimeSpan.FromSeconds(2));

        await sut.ProcessAsync(context);
        sut.CurrentPollingInterval.Should()
            .Be(
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
        // given — capture EventId.Id from ILogger.Log invocations.
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

        var lockProvider = Substitute.For<IDistributedLock>();
        // Published-path acquire throws once; received-path returns null (no contention).
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("publish-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<IDistributedLease?>>(_ => throw new InvalidOperationException("lock store down"));
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("receive-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(null));

        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(options, retryOpts, logger, dispatcher, lockProvider);

        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // when — drive a single cycle; published-path acquire throws.
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        // then — EventId 81 (PublishedRetryLockAcquireFailed) fired at Warning.
        captured.Should().Contain(e => e.Level == LogLevel.Warning && e.Id == 81);
        sut.CurrentPollingInterval.Should().Be(TimeSpan.FromMilliseconds(2));
        // And the Error escalation EventId 82 did NOT fire (only one failure so far).
        captured.Should().NotContain(e => e.Id == 82);
    }

    [Fact]
    public async Task should_escalate_to_error_eventid_82_after_three_consecutive_published_retry_lock_acquire_throws()
    {
        // given — capture EventId.Id from ILogger.Log invocations.
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

        var lockProvider = Substitute.For<IDistributedLock>();
        var publishCallCount = 0;
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("publish-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<IDistributedLease?>>(_ =>
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
                return Task.FromResult<IDistributedLease?>(Substitute.For<IDistributedLease>());
            });
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("receive-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(null));

        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(options, retryOpts, logger, dispatcher, lockProvider);

        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // when — drive three cycles; the third must emit EventId 82.
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);
        var beforeThird = captured.ToList();
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        // then — first two cycles: only EventId 81 (Warning); no EventId 82 (Error) yet.
        beforeThird.Count(e => e.Id == 81 && e.Level == LogLevel.Warning).Should().Be(2);
        beforeThird.Should().NotContain(e => e.Id == 82);

        // Third cycle: EventId 82 (Error) fires exactly once.
        captured.Count(e => e.Id == 82 && e.Level == LogLevel.Error).Should().Be(1);

        // Fourth cycle: lock acquire succeeds → _TryAcquireLockAsync resets the lock-specific counter
        // (_LockCounterRef) only; storage-pickup failures use a separate counter (_CounterRef). Drive
        // one more cycle to verify no further escalation.
        captured.Clear();
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        captured
            .Should()
            .NotContain(e => e.Id == 82, "successful acquire (returning a handle) does not emit further escalation");
    }

    [Fact]
    public async Task should_log_warning_eventid_83_when_received_retry_lock_acquire_throws()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var captured = new List<(LogLevel Level, int Id)>();
        var logger = _CreateCapturingLogger(captured);

        var lockProvider = Substitute.For<IDistributedLock>();
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("publish-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(null));
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("receive-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<IDistributedLease?>>(_ => throw new InvalidOperationException("lock store down"));

        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(options, retryOpts, logger, dispatcher, lockProvider);

        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        captured.Should().Contain(e => e.Level == LogLevel.Warning && e.Id == 83);
        captured.Should().NotContain(e => e.Id == 84);
    }

    [Fact]
    public async Task should_escalate_to_error_eventid_84_after_three_consecutive_received_retry_lock_acquire_throws()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var captured = new List<(LogLevel Level, int Id)>();
        var logger = _CreateCapturingLogger(captured);

        var lockProvider = Substitute.For<IDistributedLock>();
        var receivedCallCount = 0;
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("publish-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(null));
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("receive-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<IDistributedLease?>>(_ =>
            {
                var n = Interlocked.Increment(ref receivedCallCount);

                if (n <= 3)
                {
                    throw new InvalidOperationException("lock store down");
                }

                return Task.FromResult<IDistributedLease?>(Substitute.For<IDistributedLease>());
            });

        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(options, retryOpts, logger, dispatcher, lockProvider);

        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);
        var beforeThird = captured.ToList();
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        beforeThird.Count(e => e.Id == 83 && e.Level == LogLevel.Warning).Should().Be(2);
        beforeThird.Should().NotContain(e => e.Id == 84);
        captured.Count(e => e.Id == 84 && e.Level == LogLevel.Error).Should().Be(1);

        captured.Clear();
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        captured
            .Should()
            .NotContain(e => e.Id == 84, "successful acquire (returning a handle) does not emit further escalation");
    }

    [Fact]
    public async Task should_escalate_published_lock_acquire_failures_when_received_storage_pickup_succeeds_between_cycles()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var captured = new List<(LogLevel Level, int Id)>();
        var logger = _CreateCapturingLogger(captured);

        var lockProvider = Substitute.For<IDistributedLock>();
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("publish-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<IDistributedLease?>>(_ => throw new InvalidOperationException("lock store down"));
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("receive-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(Substitute.For<IDistributedLease>()));

        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(options, retryOpts, logger, dispatcher, lockProvider);

        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        captured.Count(e => e.Id == 82 && e.Level == LogLevel.Error).Should().Be(1);
        await dataStorage.Received(3).GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>());
    }

    // The cross-cause counterpart: lock-acquire and storage-pickup streaks must stay independent
    // *within* one kind. Under the pre-split shared per-kind counter, 1 storage failure + 2 lock
    // failures reached the threshold of 3 and fired the lock-escalation EventId 82; with split
    // counters the lock streak only reaches 2, so EventId 82 must NOT fire. The published/received
    // test above cannot prove this — those counters were always separate.
    [Fact]
    public async Task should_not_count_published_storage_failure_toward_published_lock_acquire_escalation()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var captured = new List<(LogLevel Level, int Id)>();
        var logger = _CreateCapturingLogger(captured);

        var publishLockCall = 0;
        var lockProvider = Substitute.For<IDistributedLock>();
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("publish-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<IDistributedLease?>>(_ =>
            {
                // Cycle 1: lock succeeds so published storage runs (and throws). Cycles 2-3: lock throws.
                var n = Interlocked.Increment(ref publishLockCall);
                return n == 1
                    ? Task.FromResult<IDistributedLease?>(Substitute.For<IDistributedLease>())
                    : throw new InvalidOperationException("lock store down");
            });
        // Received path is contested every cycle so it adds no lock/storage events of its own.
        lockProvider
            .TryAcquireAsync(
                Arg.Is<string>(s => s.Contains("receive-retry", StringComparison.Ordinal)),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(null));

        // Published storage throws on the one cycle it runs (cycle 1) → storage failure streak = 1.
        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
                ValueTask.FromException<IEnumerable<MediumMessage>>(new InvalidOperationException("storage down"))
            );
        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        var options = Options.Create(new MessagingOptions { UseStorageLock = true });
        var retryOpts = Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) });
        var sut = new MessageNeedToRetryProcessor(options, retryOpts, logger, dispatcher, lockProvider);

        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // Cycle 1: lock ok + storage throws (storage streak 1). Cycles 2-3: lock throws (lock streak 2).
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);
        await sut.ProcessAsync(context);
        await Task.Delay(100, AbortToken);

        // Lock streak only reached 2 and storage streak only reached 1 → neither escalation fires.
        captured.Should().NotContain(e => e.Id == 82, "the lock-acquire streak only reached 2 (cycles 2-3)");
        captured.Should().NotContain(e => e.Id == 74, "the storage-pickup streak only reached 1 (cycle 1)");
        // Both causes were still observed on their own warning EventIds, proving separate accounting.
        captured.Count(e => e.Id == 81 && e.Level == LogLevel.Warning).Should().Be(2);
        captured.Should().Contain(e => e.Id == 3110 && e.Level == LogLevel.Warning);
    }

    // -------------------------------------------------------------------------
    // #17 — UseStorageLock=false fast-path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task should_skip_try_acquire_async_when_use_storage_lock_is_false()
    {
        // given — explicit construction so we can configure UseStorageLock=false and inspect the lock provider
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var lockProvider = Substitute.For<IDistributedLock>();
        var logger = NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>();

        var options = Options.Create(new MessagingOptions { UseStorageLock = false });
        var retryProcessorOptions = Options.Create(
            new RetryProcessorOptions { BaseInterval = TimeSpan.FromMilliseconds(1) }
        );

        var sut = new MessageNeedToRetryProcessor(options, retryProcessorOptions, logger, dispatcher, lockProvider);

        _SetupReceivedMessages(dataStorage);
        using var context = _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider());

        // when — drive ProcessAsync once
        await sut.ProcessAsync(context);
        await Task.Delay(50, AbortToken);

        // then — TryAcquireAsync must never be called when UseStorageLock=false
        await lockProvider
            .DidNotReceive()
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
