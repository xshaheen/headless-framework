// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Reliability;
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

        var state = RetryHelper.ResolveNextState(MessagingRetryDecision.Stop, inlineRetries: 0, policy);

        state.IsInlineRetryInFlight.Should().BeFalse();
        state.NextRetry.Should().BeNull();
        state.NextStatus.Should().Be(StatusName.Failed);
    }

    [Fact]
    public void should_null_out_next_retry_at_for_exhausted_when_resolve_next_state()
    {
        var policy = _Policy(maxRetryAttempts: 2);

        var state = RetryHelper.ResolveNextState(MessagingRetryDecision.Exhausted, inlineRetries: 0, policy);

        state.IsInlineRetryInFlight.Should().BeFalse();
        state.NextRetry.Should().BeNull();
        state.NextStatus.Should().Be(StatusName.Failed);
    }

    [Fact]
    public void should_use_strategy_delay_exactly_for_persisted_transition_when_resolve_next_state()
    {
        // inline budget consumed (inlineRetries >= MaxRetryAttempts) — Continue routes through
        // persistence; the row MUST fall due exactly the strategy's delay after the store's clock
        // (no padding, no preservation) because the retry processor drives pickup from it.
        var policy = _Policy(maxRetryAttempts: 2);
        policy.InitialDispatchGrace = TimeSpan.FromSeconds(30);
        var decision = MessagingRetryDecision.Continue(TimeSpan.FromMinutes(5));

        var state = RetryHelper.ResolveNextState(decision, inlineRetries: 2, policy);

        state.IsInlineRetryInFlight.Should().BeFalse();
        state.NextStatus.Should().Be(StatusName.Failed);
        state.NextRetry.Should().Be(RetryDelay.Exactly(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void should_pad_resume_by_initial_dispatch_grace_when_resolve_next_state_inline_in_flight()
    {
        // Inline budget still has slots; status stays Scheduled and the due time is padded past
        // the inline-retry resume point by InitialDispatchGrace so the polling cycle does not race
        // the inline path mid-sleep. AtLeast lets the store keep a later schedule already on the row
        // (InitialDispatchGrace from initial store must not be lowered by a smaller inline delay).
        var policy = _Policy(maxRetryAttempts: 2);
        policy.InitialDispatchGrace = TimeSpan.FromSeconds(30);
        var decision = MessagingRetryDecision.Continue(TimeSpan.FromSeconds(2));

        var state = RetryHelper.ResolveNextState(decision, inlineRetries: 0, policy);

        state.IsInlineRetryInFlight.Should().BeTrue();
        state.NextStatus.Should().Be(StatusName.Scheduled);
        state.NextRetry.Should().Be(RetryDelay.AtLeast(TimeSpan.FromSeconds(32)));
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
        // Delay equal to DispatchTimeout — would oversleep the lease.
        var decision = MessagingRetryDecision.Continue(policy.DispatchTimeout);

        // inlineRetries=0 with MaxRetryAttempts=5 normally yields IsInlineRetryInFlight=true.
        // The DispatchTimeout guard must override that.
        var state = RetryHelper.ResolveNextState(decision, inlineRetries: 0, policy);

        state
            .IsInlineRetryInFlight.Should()
            .BeFalse(
                "Delay >= DispatchTimeout traps the message in inline-in-flight without consuming the persisted budget"
            );
        state.NextStatus.Should().Be(StatusName.Failed);
        state.NextRetry.Should().Be(RetryDelay.Exactly(policy.DispatchTimeout));
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

    // ─── Consume budget: additive immediate and delayed tiers ─────────────────────────────────

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(0, 2, false)]
    [InlineData(1, 0, false)]
    [InlineData(3, 0, false)]
    public void should_allow_immediate_retries_only_on_the_first_dispatch(
        int retries,
        int inlineRetriesCompleted,
        bool expected
    )
    {
        var budget = new ConsumeRetryBudget(_ConsumePolicy(immediate: 2, delayed: 3));

        budget.HasMoreInlineAttempts(retries, inlineRetriesCompleted).Should().Be(expected);
    }

    [Fact]
    public void should_decide_inline_then_delayed_then_exhausted_across_the_budget()
    {
        var policy = _ConsumePolicy(immediate: 2, delayed: 3);
        var budget = new ConsumeRetryBudget(policy);

        budget
            .Decide(retries: 0, inlineRetriesCompleted: 0)
            .Should()
            .Be(MessagingRetryDecision.Continue(TimeSpan.Zero));
        budget
            .Decide(retries: 0, inlineRetriesCompleted: 1)
            .Should()
            .Be(MessagingRetryDecision.Continue(TimeSpan.Zero));

        // Immediate retries spent: delayed retry n waits within the jitter band of initial × 2^(n-1).
        for (var retries = 0; retries < 3; retries++)
        {
            var decision = budget.Decide(retries, inlineRetriesCompleted: retries == 0 ? 2 : 0);
            decision.Outcome.Should().Be(MessagingRetryDecision.Kind.Continue);
            var baseDelay = TimeSpan.FromSeconds(10 * Math.Pow(2, retries));
            decision
                .Delay.Should()
                .BeCloseTo(
                    baseDelay,
                    (baseDelay * FailurePolicyDefinition.JitterFraction) + TimeSpan.FromMilliseconds(1)
                );
        }

        budget.Decide(retries: 3, inlineRetriesCompleted: 0).Should().Be(MessagingRetryDecision.Exhausted);
    }

    [Fact]
    public void should_flag_only_a_row_beyond_the_delayed_budget_as_over_budget()
    {
        var budget = new ConsumeRetryBudget(_ConsumePolicy(immediate: 2, delayed: 3));

        RetryHelper
            .DetectBudgetOverrun(retries: 3, budget)
            .Should()
            .BeNull("a row at its budget gets its final attempt");
        var overrun = RetryHelper.DetectBudgetOverrun(retries: 4, budget);
        overrun.Should().NotBeNull();
        overrun.Value.BypassClassification.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 2, false)]
    [InlineData(0, 3, true)]
    [InlineData(2, 0, false)]
    [InlineData(2, 1, true)]
    public void should_detect_a_spent_reservation_against_the_dispatch_budget(
        int retries,
        int reservedInlineAttempts,
        bool expected
    )
    {
        // A first dispatch of a 2-immediate policy reserves up to 3 attempts; a delayed pickup reserves 1.
        var budget = new ConsumeRetryBudget(_ConsumePolicy(immediate: 2, delayed: 3));

        RetryHelper
            .DetectCrashRecoveredReservation(retries, reservedInlineAttempts, budget)
            .HasValue.Should()
            .Be(expected);
    }

    [Fact]
    public void should_resolve_consume_state_from_the_same_budget_that_decided()
    {
        var budget = new ConsumeRetryBudget(_ConsumePolicy(immediate: 1, delayed: 2));
        var grace = TimeSpan.FromSeconds(30);
        var delayed = MessagingRetryDecision.Continue(TimeSpan.FromSeconds(12));

        // First dispatch, one immediate retry left: in flight, padded only by the grace.
        var inFlight = RetryHelper.ResolveNextState(
            MessagingRetryDecision.Continue(TimeSpan.Zero),
            0,
            0,
            budget,
            grace
        );
        inFlight.IsInlineRetryInFlight.Should().BeTrue();
        inFlight.NextStatus.Should().Be(StatusName.Scheduled);
        inFlight.NextRetry.Should().Be(RetryDelay.AtLeast(grace));

        // A delayed pickup never stays in flight, so its retry is persisted with the exact delay.
        var persisted = RetryHelper.ResolveNextState(delayed, retries: 1, inlineRetries: 0, budget, grace);
        persisted.IsInlineRetryInFlight.Should().BeFalse();
        persisted.NextStatus.Should().Be(StatusName.Failed);
        persisted.NextRetry.Should().Be(RetryDelay.Exactly(TimeSpan.FromSeconds(12)));

        var terminal = RetryHelper.ResolveNextState(MessagingRetryDecision.Exhausted, 2, 0, budget, grace);
        terminal.NextRetry.Should().BeNull();
        terminal.NextStatus.Should().Be(StatusName.Failed);
    }

    private static FailurePolicyDefinition _ConsumePolicy(int immediate, int delayed)
    {
        return new FailurePolicyBuilder()
            .Immediate(immediate)
            .Delayed(delayed, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10))
            .Build();
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

    private static FailedInfo _FailedInfo()
    {
        return new()
        {
            Message = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), null),
            MessageType = MessageType.Subscribe,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Exception = new InvalidOperationException("orig"),
            StorageId = Guid.Empty,
            RetryCount = 0,
            Lane = MessageLane.Bus,
        };
    }

    private static Guid _Guid(byte last)
    {
        return _Guid(0, last);
    }

    private static Guid _Guid(byte penultimate, byte last)
    {
        return new(0, 0, 0, 0, 0, 0, 0, 0, 0, penultimate, last);
    }
}
