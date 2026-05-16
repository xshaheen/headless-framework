// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;

namespace Tests.Retry;

public sealed class ExponentialBackoffStrategyTests : TestBase
{
    private static readonly TimeoutException _Transient = new("Transient");

    [Fact]
    public void should_calculate_exponential_delay()
    {
        // given
        var initialDelay = TimeSpan.FromSeconds(1);
        var strategy = new ExponentialBackoffStrategy(initialDelay, TimeSpan.FromMinutes(10), 2.0);

        // when
        var decision0 = strategy.Compute(0, _Transient);
        var decision1 = strategy.Compute(1, _Transient);
        var decision2 = strategy.Compute(2, _Transient);
        var decision3 = strategy.Compute(3, _Transient);

        // then - base delays (before jitter) are 1, 2, 4, 8 seconds, ±25%
        decision0.Outcome.Should().Be(RetryDecision.Kind.Continue);
        decision0.Delay.TotalSeconds.Should().BeGreaterThanOrEqualTo(0.75).And.BeLessThanOrEqualTo(1.25);

        decision1.Outcome.Should().Be(RetryDecision.Kind.Continue);
        decision1.Delay.TotalSeconds.Should().BeGreaterThanOrEqualTo(1.5).And.BeLessThanOrEqualTo(2.5);

        decision2.Outcome.Should().Be(RetryDecision.Kind.Continue);
        decision2.Delay.TotalSeconds.Should().BeGreaterThanOrEqualTo(3).And.BeLessThanOrEqualTo(5);

        decision3.Outcome.Should().Be(RetryDecision.Kind.Continue);
        decision3.Delay.TotalSeconds.Should().BeGreaterThanOrEqualTo(6).And.BeLessThanOrEqualTo(10);
    }

    [Fact]
    public void should_apply_jitter()
    {
        // given
        var strategy = new ExponentialBackoffStrategy(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10), 1.0);
        var delays = new List<double>();

        // when - run multiple times to get different jitter values
        for (var i = 0; i < 100; i++)
        {
            delays.Add(strategy.Compute(0, _Transient).Delay.TotalSeconds);
        }

        // then - delays should vary due to jitter; with ±25% on 10s, values in [7.5, 12.5]
        delays.Should().AllSatisfy(d => d.Should().BeGreaterThanOrEqualTo(7.5).And.BeLessThanOrEqualTo(12.5));

        var distinctDelays = delays.Distinct().ToList();
        distinctDelays.Count.Should().BeGreaterThan(1, "jitter should cause variance in delays");
    }

    [Fact]
    public void should_cap_at_max_delay()
    {
        // given
        var maxDelay = TimeSpan.FromSeconds(5);
        var strategy = new ExponentialBackoffStrategy(TimeSpan.FromSeconds(1), maxDelay, 2.0);

        // when - after many retries, exponential would be huge
        var decision = strategy.Compute(20, _Transient);

        // then - should be capped at max delay (±25% jitter)
        decision.Outcome.Should().Be(RetryDecision.Kind.Continue);
        decision.Delay.TotalSeconds.Should().BeLessThanOrEqualTo(maxDelay.TotalSeconds * 1.25);
        decision.Delay.TotalSeconds.Should().BeGreaterThanOrEqualTo(maxDelay.TotalSeconds * 0.75);
    }

    [Fact]
    public void should_be_thread_safe()
    {
        // given
        var strategy = new ExponentialBackoffStrategy(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), 2.0);
        var results = new ConcurrentBag<RetryDecision>();
        var exceptions = new ConcurrentBag<Exception>();

        // when - multiple threads access the strategy concurrently
        Parallel.For(
            0,
            1000,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            i =>
            {
                try
                {
                    results.Add(strategy.Compute(i % 10, _Transient));
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        );

        // then - no exceptions should occur
        exceptions.Should().BeEmpty("strategy should be thread-safe");
        results.Should().HaveCount(1000);
        results.Should().AllSatisfy(r => r.Outcome.Should().Be(RetryDecision.Kind.Continue));
    }

    [Fact]
    public void should_return_stop_for_permanent_exceptions()
    {
        // given
        var strategy = new ExponentialBackoffStrategy();

        // when/then - permanent exceptions should not be retried
        strategy.Compute(0, new SubscriberNotFoundException("Not found")).Should().Be(RetryDecision.Stop);
        strategy.Compute(0, new ArgumentNullException("value")).Should().Be(RetryDecision.Stop);
        strategy.Compute(0, new ArgumentException("Invalid arg", "value")).Should().Be(RetryDecision.Stop);
        strategy.Compute(0, new InvalidOperationException("Invalid op")).Should().Be(RetryDecision.Stop);
        strategy.Compute(0, new NotSupportedException("Not supported")).Should().Be(RetryDecision.Stop);
    }

    [Fact]
    public void should_retry_transient_exceptions()
    {
        // given
        var strategy = new ExponentialBackoffStrategy();

        // when/then - transient exceptions should be retried
        strategy.Compute(0, new TimeoutException("Timeout")).Outcome.Should().Be(RetryDecision.Kind.Continue);
        strategy.Compute(0, new IOException("Network error")).Outcome.Should().Be(RetryDecision.Kind.Continue);
        strategy.Compute(0, new ApplicationException("Generic error")).Outcome.Should().Be(RetryDecision.Kind.Continue);
    }

    [Fact]
    public void should_use_default_values_when_not_specified()
    {
        // given
        var strategy = new ExponentialBackoffStrategy();

        // when
        var decision = strategy.Compute(0, _Transient);

        // then - default initial delay is 1 second with ±25% jitter
        decision.Outcome.Should().Be(RetryDecision.Kind.Continue);
        decision.Delay.TotalSeconds.Should().BeGreaterThanOrEqualTo(0.75).And.BeLessThanOrEqualTo(1.25);
    }

    [Fact]
    public void should_use_custom_backoff_multiplier()
    {
        // given
        var strategy = new ExponentialBackoffStrategy(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10), 3.0);

        // when
        var d0 = strategy.Compute(0, _Transient);
        var d1 = strategy.Compute(1, _Transient);
        var d2 = strategy.Compute(2, _Transient);

        // then - with multiplier 3: 1, 3, 9 seconds (±25% jitter)
        d0.Delay.TotalSeconds.Should().BeGreaterThanOrEqualTo(0.75).And.BeLessThanOrEqualTo(1.25);
        d1.Delay.TotalSeconds.Should().BeGreaterThanOrEqualTo(2.25).And.BeLessThanOrEqualTo(3.75);
        d2.Delay.TotalSeconds.Should().BeGreaterThanOrEqualTo(6.75).And.BeLessThanOrEqualTo(11.25);
    }

    [Fact]
    public void should_handle_zero_retry_attempt()
    {
        // given
        var strategy = new ExponentialBackoffStrategy(TimeSpan.FromSeconds(2));

        // when
        var decision = strategy.Compute(0, _Transient);

        // then - 2^0 = 1, so delay = initialDelay * 1 = 2 seconds (±25%)
        decision.Outcome.Should().Be(RetryDecision.Kind.Continue);
        decision.Delay.TotalSeconds.Should().BeGreaterThanOrEqualTo(1.5).And.BeLessThanOrEqualTo(2.5);
    }

    [Fact]
    public void should_never_return_negative_delay()
    {
        // given
        var strategy = new ExponentialBackoffStrategy(TimeSpan.FromMilliseconds(1));

        // when - run many times to ensure jitter never causes negative
        for (var i = 0; i < 1000; i++)
        {
            var decision = strategy.Compute(0, _Transient);

            // then
            decision.Outcome.Should().Be(RetryDecision.Kind.Continue);
            decision.Delay.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(0);
        }
    }
}
