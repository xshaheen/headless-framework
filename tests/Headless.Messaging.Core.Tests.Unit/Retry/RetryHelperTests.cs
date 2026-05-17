// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.Retry;

public sealed class RetryHelperTests : TestBase
{
    [Fact]
    public void should_stop_for_permanent_exception()
    {
        var message = _CreateMessage();

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new ArgumentException("permanent", "param"),
            new RetryPolicyOptions(),
            inlineRetries: 0
        );

        decision.Should().Be(RetryDecision.Stop);
        message.Retries.Should().Be(0);
    }

    [Fact]
    public void should_return_exhausted_when_both_budgets_consumed()
    {
        // Inline budget = 0 (no inline retries), persisted budget = 0 (no persisted retries).
        // Total budget = (0+1) × (0+1) = 1 attempt. First failure exhausts.
        var message = _CreateMessage();
        var policy = new RetryPolicyOptions
        {
            MaxInlineRetries = 0,
            MaxPersistedRetries = 0,
            BackoffStrategy = new AlwaysRetryStrategy(TimeSpan.Zero),
        };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            inlineRetries: 0
        );

        decision.Should().Be(RetryDecision.Exhausted);
        message.Retries.Should().Be(0, "helper is pure with respect to MediumMessage");
    }

    [Fact]
    public void should_continue_when_inline_budget_remains_even_if_persisted_budget_consumed()
    {
        // Persisted budget consumed (Retries == MaxPersistedRetries), but the current dispatch
        // still has 2 inline retries remaining. The helper must NOT exhaust yet — inline burst
        // is the final chance on the last persisted pickup.
        var message = _CreateMessage();
        message.Retries = 5;
        var policy = new RetryPolicyOptions
        {
            MaxInlineRetries = 2,
            MaxPersistedRetries = 5,
            BackoffStrategy = new AlwaysRetryStrategy(TimeSpan.Zero),
        };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            inlineRetries: 0
        );

        decision.Outcome.Should().Be(RetryDecision.Kind.Continue);
    }

    [Fact]
    public void should_exhaust_after_inline_burst_on_terminal_pickup()
    {
        // Same setup as above, but inlineRetries has reached MaxInlineRetries — the inline burst
        // is consumed AND persisted is consumed. Now exhaust.
        var message = _CreateMessage();
        message.Retries = 5;
        var policy = new RetryPolicyOptions
        {
            MaxInlineRetries = 2,
            MaxPersistedRetries = 5,
            BackoffStrategy = new AlwaysRetryStrategy(TimeSpan.Zero),
        };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            inlineRetries: 2
        );

        decision.Should().Be(RetryDecision.Exhausted);
    }

    [Fact]
    public void should_stop_when_strategy_returns_stop()
    {
        var message = _CreateMessage();
        var policy = new RetryPolicyOptions { BackoffStrategy = new StopStrategy() };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            inlineRetries: 0
        );

        decision.Should().Be(RetryDecision.Stop);
        message.Retries.Should().Be(0);
    }

    [Fact]
    public void should_return_exhausted_when_strategy_returns_exhausted()
    {
        // Strategy-returned Exhausted is unusual but the helper passes it through.
        var message = _CreateMessage();
        var strategy = Substitute.For<IRetryBackoffStrategy>();
        strategy.Compute(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<Exception>()).Returns(RetryDecision.Exhausted);
        var policy = new RetryPolicyOptions { BackoffStrategy = strategy };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            inlineRetries: 0
        );

        decision.Outcome.Should().Be(RetryDecision.Kind.Exhausted);
        message.Retries.Should().Be(0);
    }

    [Fact]
    public void should_continue_with_computed_delay_when_retry_remains()
    {
        var message = _CreateMessage();
        var delay = TimeSpan.FromSeconds(3);
        var policy = new RetryPolicyOptions { BackoffStrategy = new AlwaysRetryStrategy(delay) };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            inlineRetries: 0
        );

        decision.Outcome.Should().Be(RetryDecision.Kind.Continue);
        decision.Delay.Should().Be(delay);
        message.Retries.Should().Be(0, "helper does not mutate MediumMessage — call site owns increments");
    }

    [Fact]
    public void should_pass_persisted_retry_count_to_backoff_strategy()
    {
        var message = _CreateMessage();
        message.Retries = 3;
        var strategy = new RecordingRetryBackoffStrategy();
        var policy = new RetryPolicyOptions { BackoffStrategy = strategy };

        RetryHelper.RecordAttemptAndComputeDecision(message, new TimeoutException(), policy, inlineRetries: 0);

        strategy.Attempts.Should().ContainSingle().Which.Should().Be(3);
    }

    [Fact]
    public void should_clamp_negative_delay_to_zero()
    {
        var message = _CreateMessage();
        var policy = new RetryPolicyOptions { BackoffStrategy = new AlwaysRetryStrategy(TimeSpan.FromSeconds(-5)) };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            inlineRetries: 0
        );

        decision.Outcome.Should().Be(RetryDecision.Kind.Continue);
        decision.Delay.Should().Be(TimeSpan.Zero, "negative delay must be clamped to zero");
    }

    [Fact]
    public void should_clamp_delay_over_24h_to_24h()
    {
        var message = _CreateMessage();
        var policy = new RetryPolicyOptions { BackoffStrategy = new AlwaysRetryStrategy(TimeSpan.FromHours(48)) };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            inlineRetries: 0
        );

        decision.Outcome.Should().Be(RetryDecision.Kind.Continue);
        decision.Delay.Should().Be(TimeSpan.FromHours(24), "delay above 24 h must be clamped to 24 h");
    }

    [Fact]
    public void retry_decision_states_should_not_compare_equal()
    {
        RetryDecision.Stop.Should().NotBe(RetryDecision.Exhausted);
        RetryDecision.Stop.Should().NotBe(RetryDecision.Continue(TimeSpan.Zero));
    }

    [Fact]
    public async Task pickup_predicate_should_exclude_failed_rows_with_null_next_retry_at()
    {
        // (StatusName=Failed, NextRetryAt=null) is the permanent-failure fingerprint produced
        // when RetryDecision.Stop fires. The pickup query MUST exclude these rows so stopped
        // messages are never re-dispatched.
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.RetryPolicy.MaxPersistedRetries = 5;
            setup.UseInMemoryMessageQueue();
            setup.UseInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IDataStorage>();

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["cap-msg-id"] = "stop-test-1" };
        var origin = new Message(headers, null);
        var stored = await storage.StoreReceivedMessageAsync("test.topic", "test-group", origin);

        // Simulate a Stop outcome: Failed status, no NextRetryAt, Retries stays below MaxPersistedRetries.
        stored.Retries = 0;
        await storage.ChangeReceiveStateAsync(stored, StatusName.Failed, nextRetryAt: null);

        var candidates = await storage.GetReceivedMessagesOfNeedRetryAsync();

        candidates.Should().BeEmpty("Stop-terminated rows must be excluded from the retry pickup query");
    }

    private static MediumMessage _CreateMessage() =>
        new()
        {
            StorageId = 1,
            Origin = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), null),
            Content = "{}",
        };

    private sealed class AlwaysRetryStrategy(TimeSpan delay) : IRetryBackoffStrategy
    {
        public RetryDecision Compute(int retryCount, int inlineRetryCount, Exception exception) =>
            RetryDecision.Continue(delay);
    }

    private sealed class StopStrategy : IRetryBackoffStrategy
    {
        public RetryDecision Compute(int retryCount, int inlineRetryCount, Exception exception) => RetryDecision.Stop;
    }

    private sealed class RecordingRetryBackoffStrategy : IRetryBackoffStrategy
    {
        public List<int> Attempts { get; } = [];

        public RetryDecision Compute(int retryCount, int inlineRetryCount, Exception exception)
        {
            Attempts.Add(retryCount);
            return RetryDecision.Continue(TimeSpan.Zero);
        }
    }

    private sealed class ThrowingBackoffStrategy : IRetryBackoffStrategy
    {
        public RetryDecision Compute(int retryCount, int inlineRetryCount, Exception exception) =>
            throw new InvalidOperationException("strategy bug");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // ─── #6: throwing strategy → Exhausted (avoid infinite consumer-invocation loop) ────────

    [Fact]
    public void throwing_strategy_should_resolve_to_exhausted()
    {
        var message = _CreateMessage();
        var policy = new RetryPolicyOptions { BackoffStrategy = new ThrowingBackoffStrategy() };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            inlineRetries: 0
        );

        decision.Outcome.Should().Be(RetryDecision.Kind.Exhausted);
    }

    // ─── #21: non-finite delay → strategy throws at TimeSpan.FromMilliseconds → caught as Exhausted ──
    //
    // TimeSpan.FromMilliseconds rejects NaN/±Infinity at construction (ArgumentException /
    // OverflowException), so a strategy that internally computes a non-finite value (e.g.,
    // backoffMultiplier = NaN) cannot RETURN a non-finite delay — it throws while constructing
    // the TimeSpan. The strategy-throw guard above is what actually catches this scenario.
    // The IsNaN / IsInfinity branch inside RecordAttemptAndComputeDecision is defensive-only.

    [Fact]
    public void strategy_that_throws_on_timespan_overflow_should_resolve_to_exhausted()
    {
        // Math.Pow(NaN, 0) is 1 by IEEE 754 convention, so retryCount=0 doesn't propagate the NaN.
        // Once persisted Retries > 0, Math.Pow(NaN, n) returns NaN, propagates to
        // TimeSpan.FromMilliseconds, throws ArgumentException, and the strategy-throw guard catches it.
        var message = _CreateMessage();
        message.Retries = 1;
        var policy = new RetryPolicyOptions
        {
            BackoffStrategy = new ExponentialBackoffStrategy(
                initialDelay: TimeSpan.FromSeconds(1),
                maxDelay: TimeSpan.FromMinutes(5),
                backoffMultiplier: double.NaN
            ),
        };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            inlineRetries: 0
        );

        decision.Outcome.Should().Be(RetryDecision.Kind.Exhausted);
    }

    // ─── #5: ResolveNextState — 3 branches plus inline-retry preservation ───────────────────

    [Fact]
    public void resolve_next_state_should_null_out_next_retry_at_for_stop()
    {
        var policy = new RetryPolicyOptions { MaxInlineRetries = 2 };
        var provider = new FixedTimeProvider(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        var state = RetryHelper.ResolveNextState(RetryDecision.Stop, inlineRetries: 0, policy, provider);

        state.IsInlineRetryInFlight.Should().BeFalse();
        state.NextRetryAt.Should().BeNull();
        state.NextStatus.Should().Be(StatusName.Failed);
    }

    [Fact]
    public void resolve_next_state_should_null_out_next_retry_at_for_exhausted()
    {
        var policy = new RetryPolicyOptions { MaxInlineRetries = 2 };
        var provider = new FixedTimeProvider(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        var state = RetryHelper.ResolveNextState(RetryDecision.Exhausted, inlineRetries: 0, policy, provider);

        state.IsInlineRetryInFlight.Should().BeFalse();
        state.NextRetryAt.Should().BeNull();
        state.NextStatus.Should().Be(StatusName.Failed);
    }

    [Fact]
    public void resolve_next_state_should_use_strategy_delay_exactly_for_persisted_transition()
    {
        // inline budget consumed (inlineRetries+1 > MaxInlineRetries) — Continue routes through
        // persistence; NextRetryAt MUST equal now+delay (no padding) because the retry processor
        // drives pickup from this timestamp.
        var policy = new RetryPolicyOptions { MaxInlineRetries = 2, InitialDispatchGrace = TimeSpan.FromSeconds(30) };
        var now = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);
        var provider = new FixedTimeProvider(now);
        var decision = RetryDecision.Continue(TimeSpan.FromMinutes(5));

        var state = RetryHelper.ResolveNextState(decision, inlineRetries: 2, policy, provider);

        state.IsInlineRetryInFlight.Should().BeFalse();
        state.NextStatus.Should().Be(StatusName.Failed);
        state.NextRetryAt.Should().Be(now.UtcDateTime.AddMinutes(5));
    }

    [Fact]
    public void resolve_next_state_inline_in_flight_should_pad_resume_by_initial_dispatch_grace()
    {
        // Inline budget still has slots; status stays Scheduled and NextRetryAt is padded past
        // the inline-retry resume point by InitialDispatchGrace so the polling cycle does not race
        // the inline path mid-sleep.
        var policy = new RetryPolicyOptions { MaxInlineRetries = 2, InitialDispatchGrace = TimeSpan.FromSeconds(30) };
        var now = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);
        var provider = new FixedTimeProvider(now);
        var decision = RetryDecision.Continue(TimeSpan.FromSeconds(2));

        var state = RetryHelper.ResolveNextState(decision, inlineRetries: 0, policy, provider);

        state.IsInlineRetryInFlight.Should().BeTrue();
        state.NextStatus.Should().Be(StatusName.Scheduled);
        state.NextRetryAt.Should().Be(now.UtcDateTime.AddSeconds(2).AddSeconds(30));
    }

    [Fact]
    public void resolve_next_state_inline_in_flight_should_preserve_existing_later_schedule()
    {
        // When currentNextRetryAt is later than (now + delay + grace), keep it — InitialDispatchGrace
        // from initial store must not be lowered by a smaller inline delay.
        var policy = new RetryPolicyOptions { MaxInlineRetries = 2, InitialDispatchGrace = TimeSpan.FromSeconds(5) };
        var now = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);
        var provider = new FixedTimeProvider(now);
        var decision = RetryDecision.Continue(TimeSpan.FromSeconds(2));
        var existing = now.UtcDateTime.AddMinutes(10);

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

    // ─── #1: IsCancellation accepts any OCE under a cancelled outer token ─────────────────────

    [Fact]
    public void is_cancellation_should_return_true_for_linked_token_oce_when_outer_cancelled()
    {
        // A linked CTS produced via CreateLinkedTokenSource carries the LINKED token on its OCE,
        // not the outer token. The previous strict identity check mis-classified this case as
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
    public void is_cancellation_should_return_true_for_matching_token()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var oce = new OperationCanceledException(cts.Token);

        RetryHelper.IsCancellation(oce, cts.Token).Should().BeTrue();
    }

    [Fact]
    public void is_cancellation_should_return_false_when_token_not_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var oce = new OperationCanceledException(cts.Token);

        RetryHelper.IsCancellation(oce, cts.Token).Should().BeFalse();
    }

    // ─── #19: InvokeOnExhaustedAsync timeout + exception absorption + CTS-on-timeout ─────────

    [Fact]
    public async Task invoke_on_exhausted_should_swallow_callback_throw_and_log()
    {
        var policy = new RetryPolicyOptions();
        var failed = new FailedInfo
        {
            Message = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), null),
            MessageType = MessageType.Subscribe,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Exception = new InvalidOperationException("orig"),
        };

        var logger = Substitute.For<ILogger>();

        // Should NOT throw — exception is logged and absorbed.
        await RetryHelper.InvokeOnExhaustedAsync(
            (_, _) => throw new InvalidOperationException("callback fault"),
            failed,
            timeout: TimeSpan.FromSeconds(1),
            storageId: 42,
            logger,
            cancellationToken: CancellationToken.None
        );

        // No exception escaped; that's the contract.
    }

    [Fact]
    public async Task invoke_on_exhausted_should_cancel_callback_token_on_host_shutdown()
    {
        // Host-shutdown OCE branch: when WaitAsync observes the supplied (host) cancellation
        // token, the callback's linked CTS must be cancelled so a cooperative callback unwinds
        // cleanly. The method must not propagate the OCE.
        var failed = new FailedInfo
        {
            Message = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), null),
            MessageType = MessageType.Subscribe,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Exception = new InvalidOperationException("orig"),
        };

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
                        // ReSharper disable once AccessToDisposedClosure
                        await hostCts.CancelAsync().ConfigureAwait(false);
                    },
                    AbortToken
                );

                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            },
            failed,
            timeout: TimeSpan.FromSeconds(30),
            storageId: 7,
            Substitute.For<ILogger>(),
            cancellationToken: hostCts.Token
        );

        // The callback's CT should have been cancelled by the OCE branch's CancelAsync call.
        var cancelled = await callbackCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancelled.Should().BeTrue();
        observedCallbackCt.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task invoke_on_exhausted_should_cancel_callback_token_on_timeout()
    {
        var policy = new RetryPolicyOptions();
        var failed = new FailedInfo
        {
            Message = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), null),
            MessageType = MessageType.Subscribe,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Exception = new InvalidOperationException("orig"),
        };

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
            storageId: 99,
            Substitute.For<ILogger>(),
            cancellationToken: CancellationToken.None
        );

        // The callback's token must have been cancelled when the timeout fired so the cooperative
        // callback can short-circuit before the dispatch scope is disposed.
        var cancelled = await observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancelled.Should().BeTrue();
    }

    // ─── #1: orphan-callback CTS disposal race — must complete with OCE, not ObjectDisposedException ─

    [Fact]
    public async Task invoke_on_exhausted_orphan_callback_should_observe_oce_not_object_disposed()
    {
        // After timeout, the callback Task is orphaned but still holds callbackCts.Token. The
        // helper must NOT dispose the CTS while the orphan is still running — otherwise the
        // orphan's `Task.Delay(_, ct)` throws ObjectDisposedException instead of OCE.
        var failed = new FailedInfo
        {
            Message = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), null),
            MessageType = MessageType.Subscribe,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Exception = new InvalidOperationException("orig"),
        };

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
            storageId: 101,
            Substitute.For<ILogger>(),
            cancellationToken: CancellationToken.None
        );

        // After helper returns (timeout fired), the orphan should eventually observe cancellation
        // through OCE — never ObjectDisposedException. CTS disposal must be deferred to after the
        // orphan completes.
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        observedException.Should().NotBeNull();
        observedException
            .Should()
            .BeAssignableTo<OperationCanceledException>(
                "the orphan must observe OCE; ObjectDisposedException indicates premature CTS disposal"
            );
    }

    // ─── #13: shutdown OCE filter widening — accept linked callbackCts.Token, not just host token ─

    [Fact]
    public async Task invoke_on_exhausted_should_treat_inner_token_oce_as_shutdown_when_host_token_cancelled()
    {
        // A cooperative callback awaiting Task.Delay(_, ct) (where ct is the linked callbackCts.Token)
        // throws an OCE bound to callbackCts.Token — not to the outer host token. Token-identity
        // against the host token alone fails. The widened filter must also accept callbackCts.Token
        // when the host token is cancelled, so the shutdown path takes effect (debug log) rather
        // than ExecutedThresholdCallbackFailed (warning).
        var failed = new FailedInfo
        {
            Message = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), null),
            MessageType = MessageType.Subscribe,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Exception = new InvalidOperationException("orig"),
        };

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
            storageId: 202,
            logger,
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
}
