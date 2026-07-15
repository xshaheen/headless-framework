// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.Retry;

public sealed class RetryHelperTests : TestBase
{
    // ─── ResolveNextState: terminal decisions clear NextRetryAt ───────────────────────────────

    [Fact]
    public void should_null_out_next_retry_at_for_stop_when_resolve_next_state()
    {
        var policy = _Policy(maxRetryAttempts: 2);
        var provider = new FixedTimeProvider(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        var state = RetryHelper.ResolveNextState(MessagingRetryDecision.Stop, inlineRetries: 0, policy, provider);

        state.IsInlineRetryInFlight.Should().BeFalse();
        state.NextRetryAt.Should().BeNull();
        state.NextStatus.Should().Be(StatusName.Failed);
    }

    [Fact]
    public void should_null_out_next_retry_at_for_exhausted_when_resolve_next_state()
    {
        var policy = _Policy(maxRetryAttempts: 2);
        var provider = new FixedTimeProvider(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        var state = RetryHelper.ResolveNextState(MessagingRetryDecision.Exhausted, inlineRetries: 0, policy, provider);

        state.IsInlineRetryInFlight.Should().BeFalse();
        state.NextRetryAt.Should().BeNull();
        state.NextStatus.Should().Be(StatusName.Failed);
    }

    [Fact]
    public void should_use_strategy_delay_exactly_for_persisted_transition_when_resolve_next_state()
    {
        // inline budget consumed (inlineRetries >= MaxRetryAttempts) — Continue routes through
        // persistence; NextRetryAt MUST equal now+delay (no padding) because the retry processor
        // drives pickup from this timestamp.
        var policy = _Policy(maxRetryAttempts: 2);
        policy.InitialDispatchGrace = TimeSpan.FromSeconds(30);
        var now = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);
        var provider = new FixedTimeProvider(now);
        var decision = MessagingRetryDecision.Continue(TimeSpan.FromMinutes(5));

        var state = RetryHelper.ResolveNextState(decision, inlineRetries: 2, policy, provider);

        state.IsInlineRetryInFlight.Should().BeFalse();
        state.NextStatus.Should().Be(StatusName.Failed);
        state.NextRetryAt.Should().Be(now.AddMinutes(5));
    }

    [Fact]
    public void should_pad_resume_by_initial_dispatch_grace_when_resolve_next_state_inline_in_flight()
    {
        // Inline budget still has slots; status stays Scheduled and NextRetryAt is padded past
        // the inline-retry resume point by InitialDispatchGrace so the polling cycle does not race
        // the inline path mid-sleep.
        var policy = _Policy(maxRetryAttempts: 2);
        policy.InitialDispatchGrace = TimeSpan.FromSeconds(30);
        var now = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);
        var provider = new FixedTimeProvider(now);
        var decision = MessagingRetryDecision.Continue(TimeSpan.FromSeconds(2));

        var state = RetryHelper.ResolveNextState(decision, inlineRetries: 0, policy, provider);

        state.IsInlineRetryInFlight.Should().BeTrue();
        state.NextStatus.Should().Be(StatusName.Scheduled);
        state.NextRetryAt.Should().Be(now.AddSeconds(2).AddSeconds(30));
    }

    [Fact]
    public void should_force_persisted_path_when_resolve_next_state_delay_exceeds_dispatch_timeout()
    {
        // #1 — when Polly computes Delay >= DispatchTimeout, the inline burst must end without
        // sleeping (the lease is sized to DispatchTimeout). ResolveNextState MUST force
        // IsInlineRetryInFlight = false so the call site advances MediumMessage.Retries; otherwise
        // the row sits with NextRetryAt set but Retries unchanged across every pickup, never
        // consuming the persisted budget and never firing OnExhausted.
        var policy = _Policy(maxRetryAttempts: 5);
        policy.InitialDispatchGrace = TimeSpan.FromSeconds(30);
        policy.DispatchTimeout = TimeSpan.FromMinutes(5);
        var now = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);
        var provider = new FixedTimeProvider(now);
        // Delay equal to DispatchTimeout — would oversleep the lease.
        var decision = MessagingRetryDecision.Continue(policy.DispatchTimeout);

        // inlineRetries=0 with MaxRetryAttempts=5 normally yields IsInlineRetryInFlight=true.
        // The DispatchTimeout guard must override that.
        var state = RetryHelper.ResolveNextState(decision, inlineRetries: 0, policy, provider);

        state
            .IsInlineRetryInFlight.Should()
            .BeFalse(
                "Delay >= DispatchTimeout traps the message in inline-in-flight without consuming the persisted budget"
            );
        state.NextStatus.Should().Be(StatusName.Failed);
        state.NextRetryAt.Should().Be(now.Add(policy.DispatchTimeout));
    }

    [Fact]
    public void should_preserve_existing_later_schedule_when_resolve_next_state_inline_in_flight()
    {
        // When currentNextRetryAt is later than (now + delay + grace), keep it — InitialDispatchGrace
        // from initial store must not be lowered by a smaller inline delay.
        var policy = _Policy(maxRetryAttempts: 2);
        policy.InitialDispatchGrace = TimeSpan.FromSeconds(5);
        var now = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);
        var provider = new FixedTimeProvider(now);
        var decision = MessagingRetryDecision.Continue(TimeSpan.FromSeconds(2));
        var existing = now.AddMinutes(10);

        var state = RetryHelper.ResolveNextState(
            decision,
            inlineRetries: 0,
            policy,
            provider,
            currentNextRetryAt: existing
        );

        state.IsInlineRetryInFlight.Should().BeTrue();
        state.NextStatus.Should().Be(StatusName.Scheduled);
        state.NextRetryAt.Should().Be(existing, "existing schedule was later than padded resume — must be preserved");
    }

    // ─── DetectCrashRecoveredReservation: shared publish/consume crash-recovery sentinel ──────

    [Fact]
    public void should_return_null_while_inline_budget_remains_when_detect_crash_recovered_reservation()
    {
        var policy = _Policy(maxRetryAttempts: 2);

        // 0..MaxRetryAttempts reserved attempts (<= 2) leave budget for one more observable attempt.
        RetryHelper.DetectCrashRecoveredReservation(0, policy).Should().BeNull();
        RetryHelper.DetectCrashRecoveredReservation(2, policy).Should().BeNull();
    }

    [Fact]
    public void should_flag_reserved_final_attempt_when_detect_crash_recovered_reservation()
    {
        // InlineAttempts == MaxRetryAttempts + 1 means the process died after reserving the final
        // inline attempt — recovery must not grant a fresh attempt; it must route the row to its
        // persisted-retry or exhausted transition, bypassing user classification.
        var policy = _Policy(maxRetryAttempts: 2);

        var attempt = RetryHelper.DetectCrashRecoveredReservation(3, policy);

        attempt.Should().NotBeNull();
        attempt.Value.CanRetry.Should().BeTrue();
        attempt.Value.BypassClassification.Should().BeTrue();
        attempt.Value.Result.Exception.Should().BeOfType<InvalidOperationException>();
    }

    // ─── IsCancellation accepts any OCE under a cancelled outer token ─────────────────────────

    [Fact]
    public void should_return_true_for_linked_token_oce_when_is_cancellation_outer_cancelled()
    {
        // A linked CTS produced via CreateLinkedTokenSource carries the LINKED token on its OCE,
        // not the outer token. A strict identity check would mis-classify this case as
        // "not a cancellation" and let dispatch-during-shutdown errors flow through the retry
        // pipeline (consuming retry budget). The relaxed contract accepts the outer-token cancel
        // flag as the sole signal — any OCE while the outer token is cancelled IS a cancellation.
        using var outer = new CancellationTokenSource();
        outer.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(outer.Token);
        // linked.Token is now cancelled because outer is cancelled.
        var oce = new OperationCanceledException(linked.Token);

        RetryHelper.IsCancellation(oce, outer.Token).Should().BeTrue();
    }

    [Fact]
    public void should_return_true_for_matching_token_when_is_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var oce = new OperationCanceledException(cts.Token);

        RetryHelper.IsCancellation(oce, cts.Token).Should().BeTrue();
    }

    [Fact]
    public void should_return_false_when_is_cancellation_token_not_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var oce = new OperationCanceledException(cts.Token);

        RetryHelper.IsCancellation(oce, cts.Token).Should().BeFalse();
    }

    // ─── InvokeOnExhaustedAsync: timeout + exception absorption + CTS-on-timeout ──────────────

    [Fact]
    public async Task should_swallow_callback_throw_and_log_when_invoke_on_exhausted()
    {
        var failed = _FailedInfo();
        var logger = Substitute.For<ILogger>();

        // Should NOT throw — exception is logged and absorbed.
        await RetryHelper.InvokeOnExhaustedAsync(
            (_, _) => throw new InvalidOperationException("callback fault"),
            failed,
            timeout: TimeSpan.FromSeconds(1),
            storageId: _Guid(0x42),
            logger,
            TimeProvider.System,
            cancellationToken: AbortToken
        );

        // No exception escaped; that's the contract.
    }

    [Fact]
    public async Task should_cancel_callback_token_on_host_shutdown_when_invoke_on_exhausted()
    {
        // Host-shutdown OCE branch: when WaitAsync observes the supplied (host) cancellation
        // token, the callback's linked CTS must be cancelled so a cooperative callback unwinds
        // cleanly. The method must not propagate the OCE.
        var failed = _FailedInfo();

        using var hostCts = new CancellationTokenSource();
        var observedCallbackCt = CancellationToken.None;
        var callbackCancelled = new TaskCompletionSource<bool>();

        await RetryHelper.InvokeOnExhaustedAsync(
            async (info, ct) =>
            {
                observedCallbackCt = ct;
                ct.Register(() => callbackCancelled.TrySetResult(true));

                // Trigger host shutdown after the callback registers its cancellation hook so
                // WaitAsync observes the host token, producing an OCE bound to that token.
                _ = Task.Run(
                    async () =>
                    {
                        await Task.Delay(50, AbortToken).ConfigureAwait(false);
                        await hostCts.CancelAsync().ConfigureAwait(false);
                    },
                    AbortToken
                );

                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            },
            failed,
            timeout: TimeSpan.FromSeconds(30),
            storageId: _Guid(0x07),
            Substitute.For<ILogger>(),
            TimeProvider.System,
            cancellationToken: hostCts.Token
        );

        // The callback's CT should have been cancelled by the OCE branch's CancelAsync call.
        var cancelled = await callbackCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        cancelled.Should().BeTrue();
        observedCallbackCt.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_cancel_callback_token_on_timeout_when_invoke_on_exhausted()
    {
        var failed = _FailedInfo();
        var observedCancellation = new TaskCompletionSource<bool>();

        await RetryHelper.InvokeOnExhaustedAsync(
            async (_, ct) =>
            {
                ct.Register(() => observedCancellation.TrySetResult(true));
                // Park forever so the timeout fires.
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            },
            failed,
            timeout: TimeSpan.FromMilliseconds(100),
            storageId: _Guid(0x99),
            Substitute.For<ILogger>(),
            TimeProvider.System,
            cancellationToken: AbortToken
        );

        // The callback's token must have been cancelled when the timeout fired so the cooperative
        // callback can short-circuit before the dispatch scope is disposed.
        var cancelled = await observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        cancelled.Should().BeTrue();
    }

    // ─── orphan-callback CTS disposal race — must complete with OCE, not ObjectDisposedException ─

    [Fact]
    public async Task should_observe_oce_not_object_disposed_when_invoke_on_exhausted_orphan_callback()
    {
        // After timeout, the callback Task is orphaned but still holds callbackCts.Token. The
        // helper must NOT dispose the CTS while the orphan is still running — otherwise the
        // orphan's `Task.Delay(_, ct)` throws ObjectDisposedException instead of OCE.
        var failed = _FailedInfo();

        Exception? observedException = null;
        var observed = new TaskCompletionSource<bool>();

        await RetryHelper.InvokeOnExhaustedAsync(
            async (_, ct) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    observedException = ex;
                    observed.TrySetResult(true);
                    throw;
                }
            },
            failed,
            timeout: TimeSpan.FromMilliseconds(50),
            storageId: _Guid(0x01, 0x01),
            Substitute.For<ILogger>(),
            TimeProvider.System,
            cancellationToken: AbortToken
        );

        // After helper returns (timeout fired), the orphan should eventually observe cancellation
        // through OCE — never ObjectDisposedException. CTS disposal must be deferred to after the
        // orphan completes.
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        observedException.Should().NotBeNull();
        observedException
            .Should()
            .BeAssignableTo<OperationCanceledException>(
                "the orphan must observe OCE; ObjectDisposedException indicates premature CTS disposal"
            );
    }

    // ─── shutdown OCE filter widening — accept linked callbackCts.Token, not just host token ──

    [Fact]
    public async Task should_treat_inner_token_oce_as_shutdown_when_invoke_on_exhausted_host_token_cancelled()
    {
        // A cooperative callback awaiting Task.Delay(_, ct) (where ct is the linked callbackCts.Token)
        // throws an OCE bound to callbackCts.Token — not to the outer host token. Token-identity
        // against the host token alone fails. The widened filter must also accept callbackCts.Token
        // when the host token is cancelled, so the shutdown path takes effect (debug log) rather
        // than ExecutedThresholdCallbackFailed (warning).
        var failed = _FailedInfo();

        // Pre-cancel the host token so the link fires immediately when WaitAsync observes it.
        using var hostCts = new CancellationTokenSource();
        await hostCts.CancelAsync();

        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        await RetryHelper.InvokeOnExhaustedAsync(
            async (_, ct) =>
            {
                // The pre-cancelled host token has propagated through the linked CTS — Task.Delay
                // raises OCE bound to the inner token, not the outer host token.
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            },
            failed,
            timeout: TimeSpan.FromSeconds(30),
            storageId: _Guid(0x02, 0x02),
            logger,
            TimeProvider.System,
            cancellationToken: hostCts.Token
        );

        // Shutdown path: OnExhaustedCallbackCancelledAtShutdown emitted, NOT
        // ExecutedThresholdCallbackFailed (which would mis-blame a cooperative callback).
        // Generated logger calls translate to ILogger.Log with EventId carrying the event name.
        var receivedCalls = logger.ReceivedCalls().ToList();
        receivedCalls
            .Should()
            .Contain(
                c =>
                    c.GetMethodInfo().Name == "Log"
                    && c.GetArguments()
                        .OfType<EventId>()
                        .Any(e =>
                            string.Equals(e.Name, "OnExhaustedCallbackCancelledAtShutdown", StringComparison.Ordinal)
                        ),
                because: "the shutdown path must emit OnExhaustedCallbackCancelledAtShutdown"
            );

        receivedCalls
            .Should()
            .NotContain(
                c =>
                    c.GetMethodInfo().Name == "Log"
                    && c.GetArguments()
                        .OfType<EventId>()
                        .Any(e => string.Equals(e.Name, "ExecutedThresholdCallbackFailed", StringComparison.Ordinal)),
                because: "a cooperative-callback OCE during shutdown must not log ExecutedThresholdCallbackFailed"
            );
    }

    private static RetryPolicyOptions _Policy(int maxRetryAttempts)
    {
        var policy = new RetryPolicyOptions();
        policy.RetryStrategy.MaxRetryAttempts = maxRetryAttempts;

        return policy;
    }

    private static FailedInfo _FailedInfo() =>
        new()
        {
            Message = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), null),
            MessageType = MessageType.Subscribe,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Exception = new InvalidOperationException("orig"),
            StorageId = Guid.Empty,
            RetryCount = 0,
            IntentType = IntentType.Bus,
        };

    private static Guid _Guid(byte last) => _Guid(0, last);

    private static Guid _Guid(byte penultimate, byte last) => new(0, 0, 0, 0, 0, 0, 0, 0, 0, penultimate, last);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
