// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Retry;

public sealed class InlineRetryLoopTests : TestBase
{
    [Fact]
    public async Task should_return_immediately_when_first_attempt_signals_stop()
    {
        var policy = new RetryPolicyOptions { MaxPersistedRetries = 4, MaxInlineRetries = 3 };
        var attempts = 0;

        var result = await InlineRetryLoop.ExecuteAsync<string>(
            (inlineRetries, _) =>
            {
                attempts++;
                return Task.FromResult((RetryDecision.Stop, "done"));
            },
            policy,
            TimeProvider.System,
            CancellationToken.None
        );

        result.Should().Be("done");
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_continue_until_attempt_returns_terminal_decision()
    {
        var policy = new RetryPolicyOptions { MaxPersistedRetries = 4, MaxInlineRetries = 5 };
        var attempts = 0;
        var observedInlineRetries = new List<int>();

        var result = await InlineRetryLoop.ExecuteAsync(
            (inlineRetries, _) =>
            {
                observedInlineRetries.Add(inlineRetries);
                attempts++;

                if (attempts == 3)
                {
                    return Task.FromResult((RetryDecision.Exhausted, attempts));
                }

                return Task.FromResult((RetryDecision.Continue(TimeSpan.Zero), attempts));
            },
            policy,
            TimeProvider.System,
            CancellationToken.None
        );

        result.Should().Be(3);
        attempts.Should().Be(3);
        observedInlineRetries.Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task should_call_attempt_once_when_max_inline_retries_is_zero()
    {
        // MaxInlineRetries=0 means no in-process retries: the loop must invoke the attempt
        // exactly once and return regardless of the decision returned.
        var policy = new RetryPolicyOptions { MaxPersistedRetries = 4, MaxInlineRetries = 0 };
        var attempts = 0;

        var result = await InlineRetryLoop.ExecuteAsync(
            (inlineRetries, _) =>
            {
                attempts++;
                return Task.FromResult((RetryDecision.Continue(TimeSpan.Zero), attempts));
            },
            policy,
            TimeProvider.System,
            CancellationToken.None
        );

        result.Should().Be(1);
        attempts.Should().Be(1, "MaxInlineRetries=0 must produce exactly one attempt");
    }

    [Fact]
    public async Task should_stop_after_max_inline_retries_even_when_decision_continues()
    {
        // Budget = 1 retry. First attempt continues, second attempt is the inline retry
        // (inlineRetries=1, still within budget), third attempt would exceed (inlineRetries=2 > 1)
        // so the loop must return after the second attempt completes.
        var policy = new RetryPolicyOptions { MaxPersistedRetries = 9, MaxInlineRetries = 1 };
        var attempts = 0;

        var result = await InlineRetryLoop.ExecuteAsync(
            (inlineRetries, _) =>
            {
                attempts++;
                return Task.FromResult((RetryDecision.Continue(TimeSpan.Zero), attempts));
            },
            policy,
            TimeProvider.System,
            CancellationToken.None
        );

        result.Should().Be(2);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task should_exit_loop_without_retry_when_delay_equals_dispatch_timeout()
    {
        // A strategy that returns Delay >= DispatchTimeout would cause the inline loop to snooze
        // past the storage lease boundary, enabling another replica to re-pick and double-dispatch
        // the same row. The guard must return the current result without incrementing inlineRetries.
        var dispatchTimeout = TimeSpan.FromMinutes(5);
        var policy = new RetryPolicyOptions { MaxInlineRetries = 3, DispatchTimeout = dispatchTimeout };
        var attempts = 0;
        var observedInlineRetries = new List<int>();

        var result = await InlineRetryLoop.ExecuteAsync(
            (inlineRetries, _) =>
            {
                observedInlineRetries.Add(inlineRetries);
                attempts++;
                // Return exactly DispatchTimeout — guard must fire and exit.
                return Task.FromResult((RetryDecision.Continue(dispatchTimeout), attempts));
            },
            policy,
            TimeProvider.System,
            CancellationToken.None
        );

        result.Should().Be(1);
        attempts.Should().Be(1, "loop must exit on the first attempt when delay >= DispatchTimeout");
        observedInlineRetries.Should().Equal(0);
    }

    [Fact]
    public async Task should_observe_cancellation_before_zero_delay_retry()
    {
        // A zero-delay strategy must not spin past cancellation: timeProvider.Delay(TimeSpan.Zero, ct)
        // can return synchronously without observing the token, so the loop checks it explicitly
        // before each inter-attempt wait. Regression guard for that pre-delay check.
        var policy = new RetryPolicyOptions { MaxPersistedRetries = 4, MaxInlineRetries = 3 };
        var attempts = 0;
        using var cts = new CancellationTokenSource();

        var act = () =>
            InlineRetryLoop.ExecuteAsync(
                async (inlineRetries, _) =>
                {
                    attempts++;
                    await cts.CancelAsync();
                    return (RetryDecision.Continue(TimeSpan.Zero), attempts);
                },
                policy,
                TimeProvider.System,
                cts.Token
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1, "the loop must not re-enter the attempt after cancellation");
    }

    [Fact]
    public async Task should_abort_pending_backoff_delay_when_cancelled()
    {
        // The inter-attempt delay must be driven through the injected TimeProvider and stay
        // cancellable while the loop is parked on it — no wall-clock waits, no missed tokens.
        var timeProvider = new FakeTimeProvider();
        var policy = new RetryPolicyOptions { MaxPersistedRetries = 4, MaxInlineRetries = 3 };
        var attempts = 0;

        // manual lifetime (not `using`): the loop task outlives this scope until awaited below (AsyncFixer04)
        var cts = new CancellationTokenSource();

        try
        {
            var loop = InlineRetryLoop.ExecuteAsync(
                (inlineRetries, _) =>
                {
                    attempts++;
                    return Task.FromResult((RetryDecision.Continue(TimeSpan.FromSeconds(30)), attempts));
                },
                policy,
                timeProvider,
                cts.Token
            );

            // the first attempt completes synchronously and the loop parks on the fake 30s delay
            attempts.Should().Be(1);
            loop.IsCompleted.Should().BeFalse();

            // when
            await cts.CancelAsync();

            // then - the parked delay unwinds as cancellation without any time advance
            var act = () => loop;
            await act.Should().ThrowAsync<OperationCanceledException>();
            attempts.Should().Be(1);
        }
        finally
        {
            cts.Dispose();
        }
    }
}
