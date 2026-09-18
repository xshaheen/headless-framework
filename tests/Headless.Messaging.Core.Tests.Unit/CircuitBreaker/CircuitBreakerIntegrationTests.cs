// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Reflection;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
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

namespace Tests.CircuitBreaker;

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
    private readonly List<IMeterFactory> _meterFactories = [];

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string _CircuitKey(string group)
    {
        return $"{MessageLane.Bus:D}:{group}";
    }

    private CircuitBreakerStateManager _CreateStateManager(
        int failureThreshold = 3,
        TimeSpan? openDuration = null,
        TimeSpan? maxOpenDuration = null,
        int successfulCyclesToResetEscalation = 3,
        TimeProvider? timeProvider = null
    )
    {
        var opts = new CircuitBreakerOptions
        {
            FailureThreshold = failureThreshold,
            OpenDuration = openDuration ?? TimeSpan.FromMilliseconds(50),
            MaxOpenDuration = maxOpenDuration ?? TimeSpan.FromSeconds(60),
            SuccessfulCyclesToResetEscalation = successfulCyclesToResetEscalation,
        };

        // Owned by the test: disposed in DisposeAsyncCore so the meter stays alive for the test.
        var meterFactory = CircuitBreakerTestHelpers.CreateMeterFactory();
        _meterFactories.Add(meterFactory);

        return new CircuitBreakerStateManager(
            Options.Create(opts),
            new ConsumerCircuitBreakerRegistry(),
            new NullLogger<CircuitBreakerStateManager>(),
            new CircuitBreakerMetrics(meterFactory),
            timeProvider ?? new FakeTimeProvider()
        );
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var meterFactory in _meterFactories)
        {
            meterFactory.Dispose();
        }

        _meterFactories.Clear();
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    private static MediumMessage _CreateMessage(string? group)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "integration.test.messageName",
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
            Lane = MessageLane.Bus,
        };
    }

    private static ProcessingContext _CreateContext(IServiceProvider? provider = null)
    {
        provider ??= new ServiceCollection().AddSingleton(Substitute.For<IDataStorage>()).BuildServiceProvider();

        return new ProcessingContext(provider, TimeProvider.System, CancellationToken.None);
    }

    /// <summary>
    /// Base retry processor interval for tests that call <c>ProcessAsync</c>.
    /// <c>ProcessAsync</c> blocks on <c>context.WaitAsync(interval)</c> at the end, so keeping
    /// this at 1 second avoids long test runs while staying above the minimum validation value.
    /// </summary>
    private const int _TestRetryIntervalSeconds = 1;

    private static void _SetupReceivedMessages(IDataStorage dataStorage, params MediumMessage[] messages)
    {
        dataStorage
            .GetReceivedInboxOrphansOfNeedRetryAsync(Arg.Any<MessageLane>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));

        dataStorage
            .GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>(messages));

        dataStorage
            .GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, Arg.Any<CancellationToken>())
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
            onPause: async epoch =>
            {
                pauseCalled = true;
            },
            onResume: epoch => ValueTask.CompletedTask
        );

        // when — 2 failures: still below threshold
        await sut.ReportFailureAsync(group, new TimeoutException("transient-1"), AbortToken);
        await sut.ReportFailureAsync(group, new TimeoutException("transient-2"), AbortToken);

        // then — circuit still closed
        sut.IsOpen(group).Should().BeFalse();
        sut.GetState(group).Should().Be(CircuitBreakerState.Closed);
        pauseCalled.Should().BeFalse();

        // when — 3rd failure hits the threshold
        await sut.ReportFailureAsync(group, new TimeoutException("transient-3"), AbortToken);

        // then — circuit opens, pause callback invoked
        sut.IsOpen(group).Should().BeTrue();
        sut.GetState(group).Should().Be(CircuitBreakerState.Open);
        pauseCalled.Should().BeTrue();

        // and — snapshot confirms open state with escalation
        var snapshot = sut.GetSnapshot(group);
        snapshot.Should().NotBeNull();
        snapshot!.State.Should().Be(CircuitBreakerState.Open);
        snapshot.OpenedAt.Should().NotBeNull();
        snapshot.EscalationLevel.Should().BePositive();
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
        var timeProvider = new FakeTimeProvider();

        await using var sut = _CreateStateManager(
            failureThreshold: 2,
            openDuration: TimeSpan.FromMilliseconds(30),
            timeProvider: timeProvider
        );

        sut.RegisterGroupCallbacks(
            group,
            onPause: epoch =>
            {
                pauseCalled = true;
                return ValueTask.CompletedTask;
            },
            onResume: epoch =>
            {
                resumeCalled = true;
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        // when — trip the circuit
        await sut.ReportFailureAsync(group, new TimeoutException("fail-1"), AbortToken);
        await sut.ReportFailureAsync(group, new TimeoutException("fail-2"), AbortToken);

        // then — circuit is open, consumer paused
        sut.IsOpen(group).Should().BeTrue();
        pauseCalled.Should().BeTrue();

        // when — wait for HalfOpen transition (resume callback fires)
        timeProvider.Advance(TimeSpan.FromMilliseconds(30));
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then — circuit is HalfOpen (still reports IsOpen=true to prevent new messages)
        sut.IsOpen(group).Should().BeTrue();
        sut.GetState(group).Should().Be(CircuitBreakerState.HalfOpen);
        resumeCalled.Should().BeTrue();

        // when — acquire probe and report success (simulating a successful message processing)
        var probeAcquired = sut.TryAcquireHalfOpenProbe(group);
        probeAcquired.Should().NotBeNull();
        await sut.ReportSuccessAsync(group, AbortToken);

        // then — circuit closes, consumer fully operational
        sut.IsOpen(group).Should().BeFalse();
        sut.GetState(group).Should().Be(CircuitBreakerState.Closed);

        // and — a second probe can't be acquired (already closed)
        // verify post-close behavior: new transient failure starts fresh counter
        await sut.ReportFailureAsync(group, new TimeoutException("post-close-1"), AbortToken);
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
        var openCircuitGroup = _CircuitKey(openGroup);
        var healthyCircuitGroup = _CircuitKey(healthyGroup);

        await using var stateManager = _CreateStateManager(failureThreshold: 1);

        stateManager.RegisterGroupCallbacks(
            openCircuitGroup,
            onPause: epoch => ValueTask.CompletedTask,
            onResume: epoch => ValueTask.CompletedTask
        );
        stateManager.RegisterGroupCallbacks(
            healthyCircuitGroup,
            onPause: epoch => ValueTask.CompletedTask,
            onResume: epoch => ValueTask.CompletedTask
        );

        // trip circuit for openGroup
        await stateManager.ReportFailureAsync(openCircuitGroup, new TimeoutException("infra down"), AbortToken);
        stateManager.IsOpen(openCircuitGroup).Should().BeTrue();
        stateManager.IsOpen(healthyCircuitGroup).Should().BeFalse();

        // wire up the retry processor with the real state manager as monitor
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var lockProvider = Substitute.For<IDistributedLock>();
        var logger = NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>();

        var retryProcessor = new MessageNeedToRetryProcessor(
            Options.Create(new MessagingOptions()),
            Options.Create(
                new RetryProcessorOptions { BaseInterval = TimeSpan.FromSeconds(_TestRetryIntervalSeconds) }
            ),
            logger,
            dispatcher,
            lockProvider,
            stateManager // real ICircuitBreakerMonitor
        );

        var openMsg1 = _CreateMessage(openGroup);
        var openMsg2 = _CreateMessage(openGroup);
        var healthyMsg = _CreateMessage(healthyGroup);
        _SetupReceivedMessages(dataStorage, openMsg1, healthyMsg, openMsg2);

        // when — process retry batch
        await using var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );
        await retryProcessor.ProcessAsync(context);

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
        var timeProvider = new FakeTimeProvider();
        var circuitGroup = _CircuitKey(group);
        var halfOpenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var stateManager = _CreateStateManager(
            failureThreshold: 2,
            openDuration: TimeSpan.FromMilliseconds(30),
            timeProvider: timeProvider
        );

        stateManager.RegisterGroupCallbacks(
            circuitGroup,
            onPause: epoch => ValueTask.CompletedTask,
            onResume: epoch =>
            {
                halfOpenTcs.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );

        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var lockProvider = Substitute.For<IDistributedLock>();
        var logger = NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>();

        var retryProcessor = new MessageNeedToRetryProcessor(
            Options.Create(new MessagingOptions()),
            Options.Create(
                new RetryProcessorOptions { BaseInterval = TimeSpan.FromSeconds(_TestRetryIntervalSeconds) }
            ),
            logger,
            dispatcher,
            lockProvider,
            stateManager
        );

        // --- Phase 1: Trip the circuit ---
        await stateManager.ReportFailureAsync(circuitGroup, new TimeoutException("fail-1"), AbortToken);
        await stateManager.ReportFailureAsync(circuitGroup, new TimeoutException("fail-2"), AbortToken);
        stateManager.IsOpen(circuitGroup).Should().BeTrue("circuit should be open after threshold failures");

        // --- Phase 2: Retry processor should skip messages while circuit is open ---
        var msg1 = _CreateMessage(group);
        _SetupReceivedMessages(dataStorage, msg1);

        await using var context1 = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );
        await retryProcessor.ProcessAsync(context1);
        await retryProcessor.WaitForQuadrantIdleForTestAsync(MessageType.Subscribe, MessageLane.Bus);
        await dispatcher.DidNotReceive().EnqueueToExecute(msg1, null, Arg.Any<CancellationToken>());

        // --- Phase 3: Wait for HalfOpen transition ---
        timeProvider.Advance(TimeSpan.FromMilliseconds(30));
        await halfOpenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        stateManager.GetState(circuitGroup).Should().Be(CircuitBreakerState.HalfOpen);

        // --- Phase 4: Probe succeeds, circuit closes ---
        await _ReportProbeSuccess(stateManager, circuitGroup);
        stateManager.IsOpen(circuitGroup).Should().BeFalse("circuit should be closed after successful probe");
        stateManager.GetState(circuitGroup).Should().Be(CircuitBreakerState.Closed);

        // --- Phase 5: Retry processor should now enqueue messages again ---
        dispatcher.ClearReceivedCalls();
        var msg2 = _CreateMessage(group);
        _SetupReceivedMessages(dataStorage, msg2);
        await using var context2 = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );
        retryProcessor.MarkQuadrantDueForTest(MessageType.Subscribe, MessageLane.Bus);
        await retryProcessor.ProcessAsync(context2);
        await retryProcessor.WaitForQuadrantIdleForTestAsync(MessageType.Subscribe, MessageLane.Bus);
        await dispatcher.Received(1).EnqueueToExecute(msg2, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task same_logical_group_keeps_lane_timers_and_recovery_independent()
    {
        // given
        const string group = "integration.group.shared";
        var busKey = CircuitBreakerGroupKeys.For(MessageLane.Bus, group);
        var queueKey = CircuitBreakerGroupKeys.For(MessageLane.Queue, group);
        var busPaused = false;
        var queuePaused = false;
        var queueResumed = false;
        var busResumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeProvider = new FakeTimeProvider();
        await using var sut = _CreateStateManager(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMilliseconds(30),
            timeProvider: timeProvider
        );

        sut.RegisterKnownGroups([busKey, queueKey]);
        sut.RegisterGroupCallbacks(
            busKey,
            onPause: epoch =>
            {
                busPaused = true;
                return ValueTask.CompletedTask;
            },
            onResume: epoch =>
            {
                busResumed.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );
        sut.RegisterGroupCallbacks(
            queueKey,
            onPause: epoch =>
            {
                queuePaused = true;
                return ValueTask.CompletedTask;
            },
            onResume: epoch =>
            {
                queueResumed = true;
                return ValueTask.CompletedTask;
            }
        );

        // when
        await sut.ReportFailureAsync(busKey, new TimeoutException("bus unavailable"), AbortToken);
        timeProvider.Advance(TimeSpan.FromMilliseconds(30));
        await busResumed.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        busPaused.Should().BeTrue();
        sut.GetState(busKey).Should().Be(CircuitBreakerState.HalfOpen);
        sut.GetState(queueKey).Should().Be(CircuitBreakerState.Closed);
        queuePaused.Should().BeFalse();
        queueResumed.Should().BeFalse();

        // when
        sut.TryAcquireHalfOpenProbe(busKey).Should().NotBeNull();
        await sut.ReportSuccessAsync(busKey, AbortToken);

        // then
        sut.GetState(busKey).Should().Be(CircuitBreakerState.Closed);
        sut.GetState(queueKey).Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public async Task timer_resume_parked_before_force_open_leaves_transport_paused()
    {
        const string group = "integration.stale-resume.timer";
        var circuitGroup = _CircuitKey(group);
        var resumeQueued = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeProvider = new FakeTimeProvider();
        await using var stateManager = _CreateStateManager(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMinutes(1),
            timeProvider: timeProvider
        );
        await using var provider = _CreateRegisterProvider();
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        var client = Substitute.For<IConsumerClient>();
        var handle = await _CreateHandleAsync(group, client);

        stateManager.RegisterGroupCallbacks(
            circuitGroup,
            onPause: epoch => _PauseHandleAsync(register, handle, epoch),
            onResume: async epoch =>
            {
                resumeQueued.TrySetResult(epoch);
                await releaseResume.Task.WaitAsync(AbortToken);
                await _ResumeHandleAsync(register, handle, epoch);
                resumeFinished.TrySetResult();
            }
        );

        await stateManager.ReportFailureAsync(circuitGroup, new TimeoutException(), AbortToken);
        await client.Received(1).PauseAsync(Arg.Any<CancellationToken>());

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        await resumeQueued.Task.WaitAsync(AbortToken);

        await stateManager.ForceOpenAsync(circuitGroup, AbortToken);
        releaseResume.TrySetResult();
        await resumeFinished.Task.WaitAsync(AbortToken);

        stateManager.GetState(circuitGroup).Should().Be(CircuitBreakerState.Open);
        await client.DidNotReceive().ResumeAsync(Arg.Any<CancellationToken>());
        await client.Received(2).PauseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task retry_resume_parked_before_force_open_is_skipped()
    {
        const string group = "integration.stale-resume.retry";
        var circuitGroup = _CircuitKey(group);
        var resumeQueued = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeProvider = new FakeTimeProvider();
        await using var stateManager = _CreateStateManager(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMinutes(1),
            timeProvider: timeProvider
        );
        await using var provider = _CreateRegisterProvider();
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        var client = Substitute.For<IConsumerClient>();
        var handle = await _CreateHandleAsync(group, client);

        stateManager.RegisterGroupCallbacks(
            circuitGroup,
            onPause: epoch => _PauseHandleAsync(register, handle, epoch),
            onResume: async epoch =>
            {
                resumeQueued.TrySetResult(epoch);
                await releaseResume.Task.WaitAsync(AbortToken);
                await _ResumeHandleAsync(register, handle, epoch);
                resumeFinished.TrySetResult();
            }
        );

        await stateManager.ReportFailureAsync(circuitGroup, new TimeoutException(), AbortToken);
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var decision = stateManager.GetRetryDecision(MessageLane.Bus, group);
        decision.Kind.Should().Be(CircuitRetryDecisionKind.ProbeAcquired);
        await resumeQueued.Task.WaitAsync(AbortToken);

        await stateManager.ForceOpenAsync(circuitGroup, AbortToken);
        releaseResume.TrySetResult();
        await resumeFinished.Task.WaitAsync(AbortToken);

        stateManager.GetState(circuitGroup).Should().Be(CircuitBreakerState.Open);
        await client.DidNotReceive().ResumeAsync(Arg.Any<CancellationToken>());
        await client.Received(2).PauseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task reset_resume_completes_before_later_force_open_pauses_again()
    {
        const string group = "integration.reset.force-open";
        var circuitGroup = _CircuitKey(group);
        var resumeCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeProvider = new FakeTimeProvider();
        await using var stateManager = _CreateStateManager(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMinutes(1),
            timeProvider: timeProvider
        );
        await using var provider = _CreateRegisterProvider();
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        var client = Substitute.For<IConsumerClient>();
        var handle = await _CreateHandleAsync(group, client);

        stateManager.RegisterGroupCallbacks(
            circuitGroup,
            onPause: epoch => _PauseHandleAsync(register, handle, epoch),
            onResume: async epoch =>
            {
                await _ResumeHandleAsync(register, handle, epoch);
                resumeCompleted.TrySetResult();
            }
        );

        await stateManager.ReportFailureAsync(circuitGroup, new TimeoutException(), AbortToken);
        await stateManager.ResetAsync(circuitGroup, AbortToken);
        await resumeCompleted.Task.WaitAsync(AbortToken);

        stateManager.GetState(circuitGroup).Should().Be(CircuitBreakerState.Closed);
        await client.Received(1).ResumeAsync(Arg.Any<CancellationToken>());

        await stateManager.ForceOpenAsync(circuitGroup, AbortToken);

        stateManager.GetState(circuitGroup).Should().Be(CircuitBreakerState.Open);
        await client.Received(2).PauseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task restart_prepause_fences_resume_launched_before_halfopen_abort()
    {
        const string group = "integration.restart.stale-resume";
        var circuitGroup = _CircuitKey(group);
        var staleResumeQueued = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStaleResume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleResumeFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeProvider = new FakeTimeProvider();
        await using var stateManager = _CreateStateManager(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMinutes(1),
            timeProvider: timeProvider
        );
        await using var provider = _CreateRegisterProvider();
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        typeof(ConsumerRegister)
            .GetField(
                "_circuitBreakerStateManager",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .SetValue(register, stateManager);
        var replacementClient = Substitute.For<IConsumerClient>();
        var legacyClient = Substitute.For<IConsumerClient>();
        var replacementHandle = await _CreateHandleAsync(group, replacementClient);
        var legacyHandle = await _CreateHandleAsync(group, legacyClient);

        stateManager.RegisterGroupCallbacks(
            circuitGroup,
            onPause: epoch => _PauseHandleAsync(register, replacementHandle, epoch),
            onResume: async epoch =>
            {
                staleResumeQueued.TrySetResult(epoch);
                await releaseStaleResume.Task.WaitAsync(AbortToken);
                await _ResumeHandleAsync(register, replacementHandle, epoch);
                staleResumeFinished.TrySetResult();
            }
        );

        await stateManager.ReportFailureAsync(circuitGroup, new TimeoutException(), AbortToken);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var staleDecision = stateManager.GetRetryDecision(MessageLane.Bus, group);
        staleDecision.Kind.Should().Be(CircuitRetryDecisionKind.ProbeAcquired);
        var staleEpoch = await staleResumeQueued.Task.WaitAsync(AbortToken);

        await stateManager.AbortHalfOpenProbeAsync(circuitGroup);
        stateManager.TryGetOpenEpoch(circuitGroup, out var restartEpoch).Should().BeTrue();
        await _PauseHandleAsync(register, replacementHandle, restartEpoch);

        releaseStaleResume.TrySetResult();
        await staleResumeFinished.Task.WaitAsync(AbortToken);
        await _ResumeHandleAsync(register, replacementHandle, staleEpoch);

        stateManager.GetState(circuitGroup).Should().Be(CircuitBreakerState.Open);
        await replacementClient.Received(2).PauseAsync(Arg.Any<CancellationToken>());
        await replacementClient.DidNotReceive().ResumeAsync(Arg.Any<CancellationToken>());
        await legacyClient.DidNotReceive().PauseAsync(Arg.Any<CancellationToken>());
        await legacyClient.DidNotReceive().ResumeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task current_resume_failure_reopens_and_pauses_transport()
    {
        const string group = "integration.resume-failure.current";
        var circuitGroup = _CircuitKey(group);
        var reopenPauseCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pauseCount = 0;
        var timeProvider = new FakeTimeProvider();
        await using var stateManager = _CreateStateManager(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMinutes(1),
            timeProvider: timeProvider
        );
        await using var provider = _CreateRegisterProvider();
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        var client = Substitute.For<IConsumerClient>();
        client
            .ResumeAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(new InvalidOperationException("resume failed")));
        var handle = await _CreateHandleAsync(group, client);

        stateManager.RegisterGroupCallbacks(
            circuitGroup,
            onPause: async epoch =>
            {
                await _PauseHandleAsync(register, handle, epoch);
                if (Interlocked.Increment(ref pauseCount) == 2)
                {
                    reopenPauseCompleted.TrySetResult();
                }
            },
            onResume: epoch => _ResumeHandleAsync(register, handle, epoch)
        );

        await stateManager.ReportFailureAsync(circuitGroup, new TimeoutException(), AbortToken);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        await reopenPauseCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        stateManager.GetState(circuitGroup).Should().Be(CircuitBreakerState.Open);
        await client.Received(1).ResumeAsync(Arg.Any<CancellationToken>());
        await client.Received(2).PauseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task stale_resume_failure_does_not_reopen_replacement_transport()
    {
        const string group = "integration.resume-failure.stale";
        var circuitGroup = _CircuitKey(group);
        var timeProvider = new FakeTimeProvider();
        await using var stateManager = _CreateStateManager(
            failureThreshold: 1,
            openDuration: TimeSpan.FromMinutes(1),
            timeProvider: timeProvider
        );
        await using var provider = _CreateRegisterProvider();
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        var replacementClient = Substitute.For<IConsumerClient>();
        var replacementHandle = await _CreateHandleAsync(group, replacementClient);

        stateManager.RegisterGroupCallbacks(
            circuitGroup,
            onPause: epoch => _PauseHandleAsync(register, replacementHandle, epoch),
            onResume: _ => ValueTask.CompletedTask
        );

        await stateManager.ReportFailureAsync(circuitGroup, new TimeoutException(), AbortToken);
        var currentEpoch = stateManager.TryAcquireHalfOpenProbe(circuitGroup)!.Value;
        await stateManager.ForceOpenAsync(circuitGroup, AbortToken);

        var reopenMethod = typeof(CircuitBreakerStateManager).GetMethod(
            "_ReopenAfterResumeFailureAsync",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(string), typeof(long)],
            null
        )!;

        await (Task)reopenMethod.Invoke(stateManager, [circuitGroup, currentEpoch])!;

        stateManager.GetState(circuitGroup).Should().Be(CircuitBreakerState.Open);
        await replacementClient.Received(1).PauseAsync(Arg.Any<CancellationToken>());
        await replacementClient.DidNotReceive().ResumeAsync(Arg.Any<CancellationToken>());
    }

    private static async Task _ReportProbeSuccess(CircuitBreakerStateManager stateManager, string group)
    {
        var probeAcquired = stateManager.TryAcquireHalfOpenProbe(group);
        probeAcquired.Should().NotBeNull("should be able to acquire probe in HalfOpen state");
        await stateManager.ReportSuccessAsync(group);
    }

    private static ServiceProvider _CreateRegisterProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
            setup.UseConventions(conventions =>
            {
                conventions.UseApplicationId("circuit-breaker-integration-tests");
                conventions.UseVersion("v1");
            });
        });

        return services.BuildServiceProvider();
    }

    private static async ValueTask<object> _CreateHandleAsync(string groupName, IConsumerClient client)
    {
        var handleType = typeof(ConsumerRegister).GetNestedType("GroupHandle", BindingFlags.NonPublic)!;
        var handle = Activator.CreateInstance(handleType, nonPublic: true)!;
        handleType.GetProperty("Logger")!.SetValue(handle, NullLogger<ConsumerRegister>.Instance);
#pragma warning disable CA2000 // The GroupHandle owns the source once it is assigned.
        handleType.GetProperty("Cts")!.SetValue(handle, new CancellationTokenSource());
#pragma warning restore CA2000
        handleType.GetProperty("GroupName")!.SetValue(handle, groupName);
        handleType.GetProperty("ConsumerTasks")!.SetValue(handle, new ConcurrentBag<Task>());
        await (ValueTask)handleType.GetMethod("AddClientAsync")!.Invoke(handle, [client])!;

        return handle;
    }

    private static async ValueTask _PauseHandleAsync(ConsumerRegister register, object handle, long epoch)
    {
        var method = typeof(ConsumerRegister).GetMethod(
            "_PauseGroupAsync",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        await (ValueTask)method.Invoke(register, [handle, epoch])!;
    }

    private static async ValueTask _ResumeHandleAsync(ConsumerRegister register, object handle, long epoch)
    {
        var method = typeof(ConsumerRegister).GetMethod(
            "_ResumeGroupAsync",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        await (ValueTask)method.Invoke(register, [handle, epoch])!;
    }
}
