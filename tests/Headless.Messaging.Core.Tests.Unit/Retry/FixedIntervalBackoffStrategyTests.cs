// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;

namespace Tests.Retry;

public sealed class FixedIntervalBackoffStrategyTests : TestBase
{
    private static readonly TimeoutException _Transient = new("Transient");

    [Fact]
    public void should_return_fixed_interval()
    {
        // given
        var interval = TimeSpan.FromSeconds(5);
        var strategy = new FixedIntervalBackoffStrategy(interval);

        // when
        var d0 = strategy.Compute(0, _Transient);
        var d1 = strategy.Compute(1, _Transient);
        var d5 = strategy.Compute(5, _Transient);
        var d100 = strategy.Compute(100, _Transient);

        // then - all decisions should be Continue with the fixed interval
        d0.Should().Be(RetryDecision.Continue(interval));
        d1.Should().Be(RetryDecision.Continue(interval));
        d5.Should().Be(RetryDecision.Continue(interval));
        d100.Should().Be(RetryDecision.Continue(interval));
    }

    [Fact]
    public void should_return_fixed_interval_for_transient_exceptions()
    {
        // given
        var interval = TimeSpan.FromSeconds(3);
        var strategy = new FixedIntervalBackoffStrategy(interval);

        // when
        var decision = strategy.Compute(0, new TimeoutException());

        // then - fixed interval for transient exceptions
        decision.Should().Be(RetryDecision.Continue(interval));
    }

    [Fact]
    public void should_return_stop_for_permanent_exceptions()
    {
        // given
        var strategy = new FixedIntervalBackoffStrategy(TimeSpan.FromSeconds(1));

        // when/then - permanent exceptions should not be retried
#pragma warning disable MA0015
        strategy.Compute(0, new SubscriberNotFoundException("Not found")).Should().Be(RetryDecision.Stop);
        strategy.Compute(0, new ArgumentNullException("value")).Should().Be(RetryDecision.Stop);
        strategy.Compute(0, new ArgumentException("Invalid arg", "value")).Should().Be(RetryDecision.Stop);
        strategy.Compute(0, new InvalidOperationException("Invalid op")).Should().Be(RetryDecision.Stop);
        strategy.Compute(0, new NotSupportedException("Not supported")).Should().Be(RetryDecision.Stop);
#pragma warning restore MA0015
    }

    [Fact]
    public void should_retry_transient_exceptions()
    {
        // given
        var strategy = new FixedIntervalBackoffStrategy(TimeSpan.FromSeconds(1));

        // when/then - transient exceptions should be retried
        strategy.Compute(0, new TimeoutException("Timeout")).ShouldRetry.Should().BeTrue();
        strategy.Compute(0, new IOException("Network error")).ShouldRetry.Should().BeTrue();
        strategy.Compute(0, new ApplicationException("Generic error")).ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public void should_handle_zero_interval()
    {
        // given
        var strategy = new FixedIntervalBackoffStrategy(TimeSpan.Zero);

        // when
        var decision = strategy.Compute(0, _Transient);

        // then
        decision.Should().Be(RetryDecision.Continue(TimeSpan.Zero));
    }

    [Fact]
    public void should_handle_small_interval()
    {
        // given
        var interval = TimeSpan.FromMilliseconds(1);
        var strategy = new FixedIntervalBackoffStrategy(interval);

        // when
        var decision = strategy.Compute(0, _Transient);

        // then
        decision.Should().Be(RetryDecision.Continue(interval));
    }

    [Fact]
    public void should_handle_large_interval()
    {
        // given
        var interval = TimeSpan.FromHours(1);
        var strategy = new FixedIntervalBackoffStrategy(interval);

        // when
        var decision = strategy.Compute(0, _Transient);

        // then
        decision.Should().Be(RetryDecision.Continue(interval));
    }

    [Fact]
    public void should_be_thread_safe()
    {
        // given
        var interval = TimeSpan.FromSeconds(2);
        var strategy = new FixedIntervalBackoffStrategy(interval);
        var results = new ConcurrentBag<RetryDecision>();

        // when
        Parallel.For(0, 1000, i => results.Add(strategy.Compute(i % 10, _Transient)));

        // then - all results should be Continue with the same fixed interval
        results.Should().HaveCount(1000);
        results.Should().AllSatisfy(r => r.Should().Be(RetryDecision.Continue(interval)));
    }
}
