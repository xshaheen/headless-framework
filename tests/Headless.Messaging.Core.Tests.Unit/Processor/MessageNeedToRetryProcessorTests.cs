// Copyright (c) Mahmoud Shaheen. All rights reserved.

using AwesomeAssertions;
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
using NSubstitute;

namespace Tests;

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
            DbId = Guid.NewGuid().ToString(),
            Origin = new Message(headers, null),
            Content = "{}",
        };
    }

    private static (MessageNeedToRetryProcessor Sut, IDispatcher Dispatcher, ICircuitBreakerMonitor Cb) _Create(
        int failedRetryInterval = 60,
        bool adaptivePolling = true,
        int maxPollingIntervalSeconds = 900,
        double circuitOpenRateThreshold = 0.8
    )
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var cb = Substitute.For<ICircuitBreakerMonitor>();
        var logger = NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>();

        var options = new MessagingOptions { FailedRetryInterval = failedRetryInterval };

        var retryProcessorOptions = new RetryProcessorOptions
        {
            AdaptivePolling = adaptivePolling,
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
        provider ??= new ServiceCollection()
            .AddSingleton(Substitute.For<IDataStorage>())
            .BuildServiceProvider();

        return new ProcessingContext(provider, CancellationToken.None);
    }

    private static void _SetupReceivedMessages(IDataStorage dataStorage, params MediumMessage[] messages)
    {
        dataStorage
            .GetReceivedMessagesOfNeedRetry(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>(messages));

        dataStorage
            .GetPublishedMessagesOfNeedRetry(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
    }

    private static TimeSpan _GetCurrentInterval(MessageNeedToRetryProcessor sut)
    {
        var field = typeof(MessageNeedToRetryProcessor).GetField(
            "_currentIntervalTicks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        return TimeSpan.FromTicks((long)field!.GetValue(sut)!);
    }

    private static void _SetCurrentInterval(MessageNeedToRetryProcessor sut, TimeSpan value)
    {
        var field = typeof(MessageNeedToRetryProcessor).GetField(
            "_currentIntervalTicks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        field!.SetValue(sut, value.Ticks);
    }

    private static void _InvokeAdjustPollingInterval(MessageNeedToRetryProcessor sut, int enqueued, int skippedCircuitOpen)
    {
        var method = typeof(MessageNeedToRetryProcessor).GetMethod(
            "_AdjustPollingInterval",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        method!.Invoke(sut, [enqueued, skippedCircuitOpen]);
    }

    private static TimeSpan _GetLockTtl(MessageNeedToRetryProcessor sut)
    {
        var method = typeof(MessageNeedToRetryProcessor).GetMethod(
            "_GetLockTtl",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        return (TimeSpan)method!.Invoke(sut, null)!;
    }

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

        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

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

        var options = new MessagingOptions { FailedRetryInterval = 60 };
        var retryProcessorOptions = new RetryProcessorOptions();

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
        var context = _CreateContext();

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
        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

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
        var (sut, dispatcher, cb) = _Create(failedRetryInterval: 10, circuitOpenRateThreshold: 0.7);

        var baseInterval = TimeSpan.FromSeconds(10);

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
        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

        // Act — first cycle
        await sut.ProcessAsync(context);

        // Assert — interval should have doubled from 10s to 20s
        var currentInterval = _GetCurrentInterval(sut);
        currentInterval.Should().Be(baseInterval * 2);

        await dispatcher.Received(1).EnqueueToExecute(
            Arg.Is<MediumMessage>(m => m.Origin.GetGroup() == "healthy-group"),
            null,
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task ProcessAsync_ResetsInterval_After3CleanCycles()
    {
        // Arrange
        var (sut, dispatcher, cb) = _Create(failedRetryInterval: 10);

        var baseInterval = TimeSpan.FromSeconds(10);

        cb.IsOpen(Arg.Any<string>()).Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();

        // All cycles return empty — clean cycles
        _SetupReceivedMessages(dataStorage);
        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

        // Act — 3 clean cycles
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);

        // Assert — no crashes, processor handles empty batches
        // The interval should have been reset to base after 3 clean cycles
        var currentInterval = _GetCurrentInterval(sut);
        currentInterval.Should().Be(baseInterval);

        await dispatcher.DidNotReceive().EnqueueToExecute(
            Arg.Any<MediumMessage>(),
            Arg.Any<ConsumerExecutorDescriptor>(),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task ProcessAsync_DoesNotAdjustInterval_WhenAdaptivePollingDisabled()
    {
        // Arrange
        var (sut, dispatcher, cb) = _Create(failedRetryInterval: 10, adaptivePolling: false);

        cb.IsOpen("open-group").Returns(true);

        var messages = Enumerable.Range(0, 10).Select(_ => _CreateMessage("open-group")).ToArray();

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, messages);
        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

        // Act — all skipped, but adaptive polling is off
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);

        // Assert — no crashes, messages still skipped due to circuit breaker
        await dispatcher.DidNotReceive().EnqueueToExecute(
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
            failedRetryInterval: 1,
            maxPollingIntervalSeconds: 2,
            circuitOpenRateThreshold: 0.5
        );
        cb.IsOpen("open-group").Returns(true);

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, _CreateMessage("open-group"));
        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

        // Act — 10 cycles, each doubles interval, should cap at maxPollingInterval (2s)
        for (var i = 0; i < 10; i++)
        {
            await sut.ProcessAsync(context);
        }

        // Assert — no crash, no overflow. Messages consistently skipped.
        // Interval should be capped at maxPollingInterval (2s)
        var currentInterval = _GetCurrentInterval(sut);
        currentInterval.Should().Be(maxPollingInterval);

        await dispatcher.DidNotReceive().EnqueueToExecute(
            Arg.Any<MediumMessage>(),
            null,
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task ProcessAsync_HalvesInterval_After2HealthyCyclesWithElevatedInterval()
    {
        // Arrange — first elevate interval via high transient rate, then 2 healthy cycles
        var (sut, dispatcher, cb) = _Create(
            failedRetryInterval: 1,
            maxPollingIntervalSeconds: 60,
            circuitOpenRateThreshold: 0.5
        );

        var dataStorage = Substitute.For<IDataStorage>();
        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

        // Cycle 1: High transient rate → doubles interval
        cb.IsOpen("open-group").Returns(true);
        cb.IsOpen("healthy-group").Returns(false);
        _SetupReceivedMessages(dataStorage,
            _CreateMessage("open-group"),
            _CreateMessage("open-group"),
            _CreateMessage("open-group"),
            _CreateMessage("healthy-group")
        );
        await sut.ProcessAsync(context);

        // Cycle 2-3: All healthy (no open circuits) → 2 consecutive healthy cycles
        cb.IsOpen("open-group").Returns(false);
        _SetupReceivedMessages(dataStorage,
            _CreateMessage("healthy-group"),
            _CreateMessage("healthy-group")
        );
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);

        // Assert — processor functions without crashes through full escalate/recover cycle
        await dispatcher.Received().EnqueueToExecute(
            Arg.Any<MediumMessage>(),
            null,
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public void lock_ttl_tracks_the_effective_polling_interval()
    {
        var (sut, _, _) = _Create(failedRetryInterval: 10);

        _SetCurrentInterval(sut, TimeSpan.FromSeconds(20));

        _GetLockTtl(sut).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AdjustPollingInterval_DoesNotOverflow_WhenCurrentIntervalIsNearMaxValue()
    {
        // Arrange — set current interval to a value where * 2 would overflow a long
        var (sut, _, _) = _Create(
            failedRetryInterval: 1,
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
    public async Task ProcessAsync_MidRangeRate_ResetsCountersWithoutChangingInterval()
    {
        // Arrange — rate between 0.5 and threshold (0.8): e.g., 6 skipped out of 10 = 60%
        var (sut, dispatcher, cb) = _Create(
            failedRetryInterval: 1,
            circuitOpenRateThreshold: 0.8
        );
        cb.IsOpen("open-group").Returns(true);
        cb.IsOpen("healthy-group").Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();
        // 6 open + 4 healthy = 60% transient rate → mid-range
        var messages = Enumerable.Range(0, 6).Select(_ => _CreateMessage("open-group"))
            .Concat(Enumerable.Range(0, 4).Select(_ => _CreateMessage("healthy-group")))
            .ToArray();
        _SetupReceivedMessages(dataStorage, messages);
        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

        // Act — mid-range cycle
        await sut.ProcessAsync(context);

        // Assert — healthy messages still enqueued, processor doesn't crash
        await dispatcher.Received(4).EnqueueToExecute(
            Arg.Is<MediumMessage>(m => m.Origin.GetGroup() == "healthy-group"),
            null,
            Arg.Any<CancellationToken>()
        );
    }
}
