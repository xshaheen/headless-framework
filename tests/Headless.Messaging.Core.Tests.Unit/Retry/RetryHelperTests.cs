// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;

namespace Tests.Retry;

public sealed class RetryHelperTests : TestBase
{
    [Fact]
    public void should_stop_without_incrementing_for_cancellation()
    {
        var message = _CreateMessage();

        var decision = RetryHelper.ComputeRetryDecision(
            message,
            new OperationCanceledException(),
            new RetryPolicyOptions(),
            isCancellation: true
        );

        decision.Should().Be(RetryDecision.Stop);
        message.Retries.Should().Be(0);
    }

    [Fact]
    public void should_stop_without_incrementing_for_permanent_exception()
    {
        var message = _CreateMessage();

        var decision = RetryHelper.ComputeRetryDecision(
            message,
            new InvalidOperationException("permanent"),
            new RetryPolicyOptions(),
            isCancellation: false
        );

        decision.Should().Be(RetryDecision.Stop);
        message.Retries.Should().Be(0);
    }

    [Fact]
    public void should_return_exhausted_when_max_attempts_reached()
    {
        var message = _CreateMessage();
        var policy = new RetryPolicyOptions
        {
            MaxAttempts = 1,
            MaxInlineRetries = 0,
            BackoffStrategy = new AlwaysRetryStrategy(TimeSpan.Zero),
        };

        var decision = RetryHelper.ComputeRetryDecision(message, new TimeoutException(), policy, isCancellation: false);

        decision.Should().Be(RetryDecision.Exhausted);
        message.Retries.Should().Be(1);
    }

    [Fact]
    public void should_stop_without_incrementing_when_strategy_returns_stop()
    {
        // Strategy's Compute returning Stop is now the unambiguous "do not retry" signal.
        // The helper must NOT increment the message's retry count for Stop outcomes — Stop
        // means the attempt never counted toward the retry budget.
        var message = _CreateMessage();
        var policy = new RetryPolicyOptions { BackoffStrategy = new StopStrategy() };

        var decision = RetryHelper.ComputeRetryDecision(message, new TimeoutException(), policy, isCancellation: false);

        decision.Should().Be(RetryDecision.Stop);
        message.Retries.Should().Be(0);
    }

    [Fact]
    public void should_continue_with_computed_delay_when_retry_remains()
    {
        var message = _CreateMessage();
        var delay = TimeSpan.FromSeconds(3);
        var policy = new RetryPolicyOptions { BackoffStrategy = new AlwaysRetryStrategy(delay) };

        var decision = RetryHelper.ComputeRetryDecision(message, new TimeoutException(), policy, isCancellation: false);

        decision.Outcome.Should().Be(RetryDecision.Kind.Continue);
        decision.Delay.Should().Be(delay);
        message.Retries.Should().Be(1);
    }

    [Fact]
    public void should_pass_zero_based_retry_attempt_to_backoff_strategy()
    {
        var message = _CreateMessage();
        var strategy = new RecordingRetryBackoffStrategy();
        var policy = new RetryPolicyOptions { BackoffStrategy = strategy };

        RetryHelper.ComputeRetryDecision(message, new TimeoutException(), policy, isCancellation: false);

        strategy.Attempts.Should().ContainSingle().Which.Should().Be(0);
        message.Retries.Should().Be(1);
    }

    [Fact]
    public void retry_decision_states_should_not_compare_equal()
    {
        RetryDecision.Stop.Should().NotBe(RetryDecision.Exhausted);
        RetryDecision.Stop.Should().NotBe(RetryDecision.Continue(TimeSpan.Zero));
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
        public RetryDecision Compute(int retryCount, Exception exception) => RetryDecision.Continue(delay);
    }

    private sealed class StopStrategy : IRetryBackoffStrategy
    {
        public RetryDecision Compute(int retryCount, Exception exception) => RetryDecision.Stop;
    }

    private sealed class RecordingRetryBackoffStrategy : IRetryBackoffStrategy
    {
        public List<int> Attempts { get; } = [];

        public RetryDecision Compute(int retryCount, Exception exception)
        {
            Attempts.Add(retryCount);
            return RetryDecision.Continue(TimeSpan.Zero);
        }
    }
}
