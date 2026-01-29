// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;

namespace Tests.Retry;

public sealed class ExponentialBackoffStrategyTests : TestBase
{
    [Fact]
    public void should_calculate_exponential_delay()
    {
        // given
        var initialDelay = TimeSpan.FromSeconds(1);
        var strategy = new ExponentialBackoffStrategy(initialDelay, TimeSpan.FromMinutes(10), 2.0);

        // when
        var delay0 = strategy.GetNextDelay(0);
        var delay1 = strategy.GetNextDelay(1);
        var delay2 = strategy.GetNextDelay(2);
        var delay3 = strategy.GetNextDelay(3);

        // then - base delays (before jitter) are 1, 2, 4, 8 seconds
        // with ±25% jitter, delay0 should be in [0.75, 1.25] seconds
        delay0.Should().NotBeNull();
        delay0!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(0.75).And.BeLessThanOrEqualTo(1.25);

        // delay1 should be in [1.5, 2.5] seconds
        delay1.Should().NotBeNull();
        delay1!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(1.5).And.BeLessThanOrEqualTo(2.5);

        // delay2 should be in [3, 5] seconds
        delay2.Should().NotBeNull();
        delay2!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(3).And.BeLessThanOrEqualTo(5);

        // delay3 should be in [6, 10] seconds
        delay3.Should().NotBeNull();
        delay3!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(6).And.BeLessThanOrEqualTo(10);
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
            var delay = strategy.GetNextDelay(0);
            delays.Add(delay!.Value.TotalSeconds);
        }

        // then - delays should vary due to jitter
        // with ±25% jitter on 10s, values should be in [7.5, 12.5]
        delays.Should().AllSatisfy(d => d.Should().BeGreaterThanOrEqualTo(7.5).And.BeLessThanOrEqualTo(12.5));

        // verify variance - not all values should be the same
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
        var delay = strategy.GetNextDelay(20);

        // then - should be capped at max delay (±25% jitter)
        delay.Should().NotBeNull();
        delay!.Value.TotalSeconds.Should().BeLessThanOrEqualTo(maxDelay.TotalSeconds * 1.25);
        delay.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(maxDelay.TotalSeconds * 0.75);
    }

    [Fact]
    public void should_be_thread_safe()
    {
        // given
        var strategy = new ExponentialBackoffStrategy(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), 2.0);
        var results = new ConcurrentBag<TimeSpan?>();
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
                    var delay = strategy.GetNextDelay(i % 10);
                    results.Add(delay);
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
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
    }

    [Fact]
    public void should_return_null_for_permanent_exceptions()
    {
        // given
        var strategy = new ExponentialBackoffStrategy();

        // when/then - permanent exceptions should not be retried
        strategy.GetNextDelay(0, new SubscriberNotFoundException("Not found")).Should().BeNull();
        strategy.GetNextDelay(0, new ArgumentNullException("value")).Should().BeNull();
        strategy.GetNextDelay(0, new ArgumentException("Invalid arg", "value")).Should().BeNull();
        strategy.GetNextDelay(0, new InvalidOperationException("Invalid op")).Should().BeNull();
        strategy.GetNextDelay(0, new NotSupportedException("Not supported")).Should().BeNull();
    }

    [Fact]
    public void should_retry_transient_exceptions()
    {
        // given
        var strategy = new ExponentialBackoffStrategy();

        // when/then - transient exceptions should be retried
        strategy.GetNextDelay(0, new TimeoutException("Timeout")).Should().NotBeNull();
        strategy.GetNextDelay(0, new IOException("Network error")).Should().NotBeNull();
        strategy.GetNextDelay(0, new ApplicationException("Generic error")).Should().NotBeNull();
    }

    [Fact]
    public void should_retry_without_exception()
    {
        // given
        var strategy = new ExponentialBackoffStrategy();

        // when
        var delay = strategy.GetNextDelay(0);

        // then - null exception means retry
        delay.Should().NotBeNull();
    }

    [Fact]
    public void should_respect_retryable_exceptions_via_should_retry()
    {
        // given
        var strategy = new ExponentialBackoffStrategy();

        // when/then - ShouldRetry method
        strategy.ShouldRetry(new SubscriberNotFoundException("Not found")).Should().BeFalse();
        strategy.ShouldRetry(new ArgumentNullException("value")).Should().BeFalse();
        strategy.ShouldRetry(new ArgumentException("Invalid", "value")).Should().BeFalse();
        strategy.ShouldRetry(new InvalidOperationException("Op")).Should().BeFalse();
        strategy.ShouldRetry(new NotSupportedException("Not")).Should().BeFalse();

        strategy.ShouldRetry(new TimeoutException("Timeout")).Should().BeTrue();
        strategy.ShouldRetry(new IOException("Network")).Should().BeTrue();
        strategy.ShouldRetry(new ApplicationException("Generic")).Should().BeTrue();
    }

    [Fact]
    public void should_use_default_values_when_not_specified()
    {
        // given
        var strategy = new ExponentialBackoffStrategy();

        // when
        var delay = strategy.GetNextDelay(0);

        // then - default initial delay is 1 second with ±25% jitter
        delay.Should().NotBeNull();
        delay!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(0.75).And.BeLessThanOrEqualTo(1.25);
    }

    [Fact]
    public void should_use_custom_backoff_multiplier()
    {
        // given
        var strategy = new ExponentialBackoffStrategy(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10), 3.0);

        // when
        var delay0 = strategy.GetNextDelay(0);
        var delay1 = strategy.GetNextDelay(1);
        var delay2 = strategy.GetNextDelay(2);

        // then - with multiplier 3: 1, 3, 9 seconds (±25% jitter)
        delay0.Should().NotBeNull();
        delay0!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(0.75).And.BeLessThanOrEqualTo(1.25);

        delay1.Should().NotBeNull();
        delay1!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(2.25).And.BeLessThanOrEqualTo(3.75);

        delay2.Should().NotBeNull();
        delay2!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(6.75).And.BeLessThanOrEqualTo(11.25);
    }

    [Fact]
    public void should_handle_zero_retry_attempt()
    {
        // given
        var strategy = new ExponentialBackoffStrategy(TimeSpan.FromSeconds(2));

        // when
        var delay = strategy.GetNextDelay(0);

        // then - 2^0 = 1, so delay = initialDelay * 1 = 2 seconds (±25%)
        delay.Should().NotBeNull();
        delay!.Value.TotalSeconds.Should().BeGreaterThanOrEqualTo(1.5).And.BeLessThanOrEqualTo(2.5);
    }

    [Fact]
    public void should_never_return_negative_delay()
    {
        // given
        var strategy = new ExponentialBackoffStrategy(TimeSpan.FromMilliseconds(1));

        // when - run many times to ensure jitter never causes negative
        for (var i = 0; i < 1000; i++)
        {
            var delay = strategy.GetNextDelay(0);

            // then
            delay.Should().NotBeNull();
            delay!.Value.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(0);
        }
    }
}
