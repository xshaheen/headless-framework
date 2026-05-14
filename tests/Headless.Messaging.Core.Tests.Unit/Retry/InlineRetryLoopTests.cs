// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;

namespace Tests.Retry;

public sealed class InlineRetryLoopTests : TestBase
{
    [Fact]
    public async Task should_return_immediately_when_first_attempt_signals_stop()
    {
        var policy = new RetryPolicyOptions { MaxAttempts = 5, MaxInlineRetries = 3 };
        var attempts = 0;

        var result = await InlineRetryLoop.ExecuteAsync<string>(
            (inlineRetries, _) =>
            {
                attempts++;
                return Task.FromResult((RetryDecision.Stop, "done"));
            },
            policy,
            CancellationToken.None
        );

        result.Should().Be("done");
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_continue_until_attempt_returns_terminal_decision()
    {
        var policy = new RetryPolicyOptions { MaxAttempts = 5, MaxInlineRetries = 5 };
        var attempts = 0;
        var observedInlineRetries = new List<int>();

        var result = await InlineRetryLoop.ExecuteAsync<int>(
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
            CancellationToken.None
        );

        result.Should().Be(3);
        attempts.Should().Be(3);
        observedInlineRetries.Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task should_stop_after_max_inline_retries_even_when_decision_continues()
    {
        // Budget = 1 retry. First attempt continues, second attempt is the inline retry
        // (inlineRetries=1, still within budget), third attempt would exceed (inlineRetries=2 > 1)
        // so the loop must return after the second attempt completes.
        var policy = new RetryPolicyOptions { MaxAttempts = 10, MaxInlineRetries = 1 };
        var attempts = 0;

        var result = await InlineRetryLoop.ExecuteAsync<int>(
            (inlineRetries, _) =>
            {
                attempts++;
                return Task.FromResult((RetryDecision.Continue(TimeSpan.Zero), attempts));
            },
            policy,
            CancellationToken.None
        );

        result.Should().Be(2);
        attempts.Should().Be(2);
    }
}
