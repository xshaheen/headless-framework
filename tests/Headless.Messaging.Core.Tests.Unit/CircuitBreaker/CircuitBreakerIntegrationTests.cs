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
using NSubstitute;

namespace Tests;

/// <summary>
/// Integration-style tests that use the real <see cref="CircuitBreakerStateManager"/> (not mocked)
/// to verify end-to-end circuit breaker flows: trip, pause, half-open probe, close, and retry
/// processor circuit-state awareness.
/// </summary>
/// <remarks>
/// These tests run without Docker — they exercise real state management with in-memory
/// components only. Placed in the unit test project because they have no external dependencies.
/// </remarks>
public sealed class CircuitBreakerIntegrationTests : TestBase
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static CircuitBreakerStateManager _CreateStateManager(
        int failureThreshold = 3,
        TimeSpan? openDuration = null,
        TimeSpan? maxOpenDuration = null,
        int successfulCyclesToResetEscalation = 3
    )
    {
        var opts = new CircuitBreakerOptions
        {
            FailureThreshold = failureThreshold,
            OpenDuration = openDuration ?? TimeSpan.FromMilliseconds(50),
            MaxOpenDuration = maxOpenDuration ?? TimeSpan.FromSeconds(60),
            SuccessfulCyclesToResetEscalation = successfulCyclesToResetEscalation,
        };

        return new CircuitBreakerStateManager(
            Options.Create(opts),
            new ConsumerCircuitBreakerRegistry(),
            new NullLogger<CircuitBreakerStateManager>(),
            new CircuitBreakerMetrics(CircuitBreakerTestHelpers.CreateMeterFactory())
        );
    }

    private static MediumMessage _CreateMessage(string? group)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "integration.test.topic",
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

    private static ProcessingContext _CreateContext(IServiceProvider? provider = null)
    {
        provider ??= new ServiceCollection()
            .AddSingleton(Substitute.For<IDataStorage>())
            .BuildServiceProvider();

        return new ProcessingContext(provider, CancellationToken.None);
    }

    /// <summary>
    /// Minimum allowed <c>FailedRetryInterval</c> for tests that call <c>ProcessAsync</c>.
    /// <c>ProcessAsync</c> blocks on <c>context.WaitAsync(interval)</c> at the end, so keeping
    /// this at 1 second avoids long test runs while staying above the minimum validation value.
    /// </summary>
    private const int _TestRetryIntervalSeconds = 1;

    private static void _SetupReceivedMessages(IDataStorage dataStorage, params MediumMessage[] messages)
    {
        dataStorage
            .GetReceivedMessagesOfNeedRetry(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>(messages));

        dataStorage
            .GetPublishedMessagesOfNeedRetry(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
    }

    // -------------------------------------------------------------------------
    // Test 1: N consecutive transient failures trip the circuit, consumer paused
    // -------------------------------------------------------------------------

    [Fact]
    public async Task consecutive_transient_failures_trip_circuit_and_pause_consumer()
    {
        // given — threshold = 3, register pause/resume callbacks
        const string group = "integration.group.trip";
        var pauseCalled = false;
        await using var sut = _CreateStateManager(failureThreshold: 3);

        sut.RegisterGroupCallbacks(
            group,
            onPause: () =>
            {
                pauseCalled = true;
                return ValueTask.CompletedTask;
            },
            onResume: () => ValueTask.CompletedTask
        );

        // when — 2 failures: still below threshold
        await sut.ReportFailureAsync(group, new TimeoutException("transient-1"));
        await sut.ReportFailureAsync(group, new TimeoutException("transient-2"));

        // then — circuit still closed
        sut.IsOpen(group).Should().BeFalse();
        sut.GetState(group).Should().Be(CircuitBreakerState.Closed);
        pauseCalled.Should().BeFalse();

        // when — 3rd failure hits the threshold
        await sut.ReportFailureAsync(group, new TimeoutException("transient-3"));

        // then — circuit opens, pause callback invoked
        sut.IsOpen(group).Should().BeTrue();
        sut.GetState(group).Should().Be(CircuitBreakerState.Open);
        pauseCalled.Should().BeTrue();

        // and — snapshot confirms open state with escalation
        var snapshot = sut.GetSnapshot(group);
        snapshot.Should().NotBeNull();
        snapshot!.State.Should().Be(CircuitBreakerState.Open);
        snapshot.OpenedAt.Should().NotBeNull();
        snapshot.EscalationLevel.Should().BeGreaterThan(0);
    }

    // -------------------------------------------------------------------------
    // Test 2: After open duration, HalfOpen probe succeeds, circuit closes,
    //         consumer resumes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task halfopen_probe_success_closes_circuit_and_resumes_consumer()
    {
        // given — short open duration so HalfOpen fires quickly
        const string group = "integration.group.recovery";
        var pauseCalled = false;
        var resumeCalled = false;
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sut = _CreateStateManager(failureThreshold: 2, openDuration: TimeSpan.FromMilliseconds(30));

        sut.RegisterGroupCallbacks(
            group,
            onPause: () =>
            {
                pauseCalled = true;
                return ValueTask.CompletedTask;
            },
            onResume: () =>
            {
                resumeCalled = true;
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        // when — trip the circuit
        await sut.ReportFailureAsync(group, new TimeoutException("fail-1"));
        await sut.ReportFailureAsync(group, new TimeoutException("fail-2"));

        // then — circuit is open, consumer paused
        sut.IsOpen(group).Should().BeTrue();
        pauseCalled.Should().BeTrue();

        // when — wait for HalfOpen transition (resume callback fires)
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // then — circuit is HalfOpen (still reports IsOpen=true to prevent new messages)
        sut.IsOpen(group).Should().BeTrue();
        sut.GetState(group).Should().Be(CircuitBreakerState.HalfOpen);
        resumeCalled.Should().BeTrue();

        // when — acquire probe and report success (simulating a successful message processing)
        var probeAcquired = sut.TryAcquireHalfOpenProbe(group);
        probeAcquired.Should().BeTrue();
        await sut.ReportSuccessAsync(group);

        // then — circuit closes, consumer fully operational
        sut.IsOpen(group).Should().BeFalse();
        sut.GetState(group).Should().Be(CircuitBreakerState.Closed);

        // and — a second probe can't be acquired (already closed)
        // verify post-close behavior: new transient failure starts fresh counter
        await sut.ReportFailureAsync(group, new TimeoutException("post-close-1"));
        sut.IsOpen(group).Should().BeFalse(); // 1 failure < threshold of 2
    }

    // -------------------------------------------------------------------------
    // Test 3: Retry processor skips re-enqueue for open-circuit group
    // -------------------------------------------------------------------------

    [Fact]
    public async Task retry_processor_skips_messages_for_open_circuit_group()
    {
        // given — use real CircuitBreakerStateManager as the ICircuitBreakerMonitor
        const string openGroup = "integration.group.open";
        const string healthyGroup = "integration.group.healthy";

        await using var stateManager = _CreateStateManager(failureThreshold: 1);

        stateManager.RegisterGroupCallbacks(
            openGroup,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );
        stateManager.RegisterGroupCallbacks(
            healthyGroup,
            onPause: () => ValueTask.CompletedTask,
            onResume: () => ValueTask.CompletedTask
        );

        // trip circuit for openGroup
        await stateManager.ReportFailureAsync(openGroup, new TimeoutException("infra down"));
        stateManager.IsOpen(openGroup).Should().BeTrue();
        stateManager.IsOpen(healthyGroup).Should().BeFalse();

        // wire up the retry processor with the real state manager as monitor
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var logger = NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>();

        var retryProcessor = new MessageNeedToRetryProcessor(
            Options.Create(new MessagingOptions { FailedRetryInterval = _TestRetryIntervalSeconds }),
            Options.Create(new RetryProcessorOptions()),
            logger,
            dispatcher,
            dataStorage,
            stateManager // real ICircuitBreakerMonitor
        );

        var openMsg1 = _CreateMessage(openGroup);
        var openMsg2 = _CreateMessage(openGroup);
        var healthyMsg = _CreateMessage(healthyGroup);
        _SetupReceivedMessages(dataStorage, openMsg1, healthyMsg, openMsg2);

        // when — process retry batch
        await retryProcessor.ProcessAsync(
            _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider())
        );

        // then — only healthyGroup message was enqueued; openGroup messages skipped
        await dispatcher.Received(1).EnqueueToExecute(healthyMsg, null, Arg.Any<CancellationToken>());
        await dispatcher.DidNotReceive().EnqueueToExecute(openMsg1, null, Arg.Any<CancellationToken>());
        await dispatcher.DidNotReceive().EnqueueToExecute(openMsg2, null, Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Test 4: Full end-to-end lifecycle: trip → open → HalfOpen → close → retry resumes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task full_lifecycle_trip_open_halfopen_close_retry_resumes()
    {
        // given — a circuit breaker and a retry processor sharing the same state manager
        const string group = "integration.group.lifecycle";
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var stateManager = _CreateStateManager(failureThreshold: 2, openDuration: TimeSpan.FromMilliseconds(30));

        stateManager.RegisterGroupCallbacks(
            group,
            onPause: () => ValueTask.CompletedTask,
            onResume: () =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var logger = NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>();

        var retryProcessor = new MessageNeedToRetryProcessor(
            Options.Create(new MessagingOptions { FailedRetryInterval = _TestRetryIntervalSeconds }),
            Options.Create(new RetryProcessorOptions()),
            logger,
            dispatcher,
            dataStorage,
            stateManager
        );

        // --- Phase 1: Trip the circuit ---
        await stateManager.ReportFailureAsync(group, new TimeoutException("fail-1"));
        await stateManager.ReportFailureAsync(group, new TimeoutException("fail-2"));
        stateManager.IsOpen(group).Should().BeTrue("circuit should be open after threshold failures");

        // --- Phase 2: Retry processor should skip messages while circuit is open ---
        var msg1 = _CreateMessage(group);
        _SetupReceivedMessages(dataStorage, msg1);

        await retryProcessor.ProcessAsync(
            _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider())
        );
        await dispatcher.DidNotReceive().EnqueueToExecute(msg1, null, Arg.Any<CancellationToken>());

        // --- Phase 3: Wait for HalfOpen transition ---
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stateManager.GetState(group).Should().Be(CircuitBreakerState.HalfOpen);

        // --- Phase 4: Probe succeeds, circuit closes ---
        await _ReportProbeSuccess(stateManager, group);
        stateManager.IsOpen(group).Should().BeFalse("circuit should be closed after successful probe");
        stateManager.GetState(group).Should().Be(CircuitBreakerState.Closed);

        // --- Phase 5: Retry processor should now enqueue messages again ---
        dispatcher.ClearReceivedCalls();
        var msg2 = _CreateMessage(group);
        _SetupReceivedMessages(dataStorage, msg2);

        await retryProcessor.ProcessAsync(
            _CreateContext(new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider())
        );
        await dispatcher.Received(1).EnqueueToExecute(msg2, null, Arg.Any<CancellationToken>());
    }

    private static async Task _ReportProbeSuccess(CircuitBreakerStateManager stateManager, string group)
    {
        var probeAcquired = stateManager.TryAcquireHalfOpenProbe(group);
        probeAcquired.Should().BeTrue("should be able to acquire probe in HalfOpen state");
        await stateManager.ReportSuccessAsync(group);
    }
}
