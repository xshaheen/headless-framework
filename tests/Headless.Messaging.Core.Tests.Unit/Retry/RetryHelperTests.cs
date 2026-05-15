// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Retry;

public sealed class RetryHelperTests : TestBase
{
    [Fact]
    public void should_stop_without_incrementing_for_cancellation()
    {
        var message = _CreateMessage();

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
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

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
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

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            isCancellation: false
        );

        decision.Should().Be(RetryDecision.Exhausted);
        message
            .Retries.Should()
            .Be(0, "Exhausted must not advance the retry counter — budget check fires before the increment");
    }

    [Fact]
    public void should_stop_without_incrementing_when_strategy_returns_stop()
    {
        // Strategy's Compute returning Stop is now the unambiguous "do not retry" signal.
        // The helper must NOT increment the message's retry count for Stop outcomes — Stop
        // means the attempt never counted toward the retry budget.
        var message = _CreateMessage();
        var policy = new RetryPolicyOptions { BackoffStrategy = new StopStrategy() };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            isCancellation: false
        );

        decision.Should().Be(RetryDecision.Stop);
        message.Retries.Should().Be(0);
    }

    [Fact]
    public void should_return_stop_without_incrementing_retries_when_max_attempts_is_one_and_exception_is_permanent()
    {
        // Permanent exception with MaxAttempts=1: the strategy classifies the exception as
        // non-retryable (returns Stop) — the helper must surface Stop and leave Retries at 0
        // (Stop means the attempt never counted toward the budget).
        var message = _CreateMessage();
        var strategy = Substitute.For<IRetryBackoffStrategy>();
        strategy.Compute(Arg.Any<int>(), Arg.Any<Exception>()).Returns(RetryDecision.Stop);
        var policy = new RetryPolicyOptions
        {
            MaxAttempts = 1,
            MaxInlineRetries = 0,
            BackoffStrategy = strategy,
        };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new ArgumentNullException("param"),
            policy,
            isCancellation: false
        );

        decision.Should().Be(RetryDecision.Stop);
        message.Retries.Should().Be(0);
    }

    [Fact]
    public void should_return_exhausted_without_incrementing_when_strategy_returns_exhausted()
    {
        // RetryDecision.Kind.Exhausted returned by a strategy is an unsupported use-case; the
        // helper treats it as Stop (no retry-counter increment). This pins that contract so a
        // future refactor cannot silently change it.
        var message = _CreateMessage();
        var strategy = Substitute.For<IRetryBackoffStrategy>();
        strategy.Compute(Arg.Any<int>(), Arg.Any<Exception>()).Returns(RetryDecision.Exhausted);
        var policy = new RetryPolicyOptions { BackoffStrategy = strategy };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            isCancellation: false
        );

        decision.Outcome.Should().Be(RetryDecision.Kind.Exhausted);
        message.Retries.Should().Be(0, "Exhausted from strategy must not advance the retry counter");
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
            isCancellation: false
        );

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

        RetryHelper.RecordAttemptAndComputeDecision(message, new TimeoutException(), policy, isCancellation: false);

        strategy.Attempts.Should().ContainSingle().Which.Should().Be(0);
        message.Retries.Should().Be(1);
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
            isCancellation: false
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
            isCancellation: false
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

    // B3 — pinning tests for the Stop-preserves-Retries invariant.

    [Fact]
    public void should_not_increment_retries_when_strategy_returns_stop_for_permanent_exception()
    {
        // Permanent failures use RetryDecision.Stop; the retry counter must stay at zero
        // so the pickup query's (Retries < MaxAttempts) guard does not re-queue stopped rows.
        var message = _CreateMessage();
        var strategy = Substitute.For<IRetryBackoffStrategy>();
        strategy.Compute(Arg.Any<int>(), Arg.Any<Exception>()).Returns(RetryDecision.Stop);

        var policy = new RetryPolicyOptions { BackoffStrategy = strategy };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new InvalidOperationException("permanent"),
            policy,
            isCancellation: false
        );

        decision.Outcome.Should().Be(RetryDecision.Kind.Stop);
        message.Retries.Should().Be(0, "Stop must never advance the retry counter");
    }

    [Fact]
    public async Task pickup_predicate_should_exclude_failed_rows_with_null_next_retry_at_below_max_attempts()
    {
        // (StatusName=Failed, NextRetryAt=null, Retries < MaxAttempts) is the permanent-failure
        // fingerprint produced when RetryDecision.Stop fires. The pickup query MUST exclude these
        // rows so stopped messages are never re-dispatched.
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(x =>
        {
            x.RetryPolicy.MaxAttempts = 5;
            x.UseInMemoryMessageQueue();
            x.UseInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IDataStorage>();

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["cap-msg-id"] = "stop-test-1" };
        var origin = new Message(headers, null);
        var stored = await storage.StoreReceivedMessageAsync("test.topic", "test-group", origin);

        // Simulate a Stop outcome: Failed status, no NextRetryAt, Retries stays below MaxAttempts.
        stored.Retries = 0;
        await storage.ChangeReceiveStateAsync(stored, StatusName.Failed, nextRetryAt: null);

        var candidates = await storage.GetReceivedMessagesOfNeedRetry();

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
