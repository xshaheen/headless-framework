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
    public void should_stop_for_cancellation()
    {
        var message = _CreateMessage();

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new OperationCanceledException(),
            new RetryPolicyOptions(),
            inlineRetries: 0,
            isCancellation: true
        );

        decision.Should().Be(RetryDecision.Stop);
        message.Retries.Should().Be(0);
    }

    [Fact]
    public void should_stop_for_permanent_exception()
    {
        var message = _CreateMessage();

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new InvalidOperationException("permanent"),
            new RetryPolicyOptions(),
            inlineRetries: 0,
            isCancellation: false
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
            inlineRetries: 0,
            isCancellation: false
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
            inlineRetries: 0,
            isCancellation: false
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
            inlineRetries: 2,
            isCancellation: false
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
            inlineRetries: 0,
            isCancellation: false
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
        strategy.Compute(Arg.Any<int>(), Arg.Any<Exception>()).Returns(RetryDecision.Exhausted);
        var policy = new RetryPolicyOptions { BackoffStrategy = strategy };

        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            inlineRetries: 0,
            isCancellation: false
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
            inlineRetries: 0,
            isCancellation: false
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

        RetryHelper.RecordAttemptAndComputeDecision(
            message,
            new TimeoutException(),
            policy,
            inlineRetries: 0,
            isCancellation: false
        );

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
            inlineRetries: 0,
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
            inlineRetries: 0,
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
