// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;

namespace Tests.Retry;

public sealed class FixedIntervalBackoffStrategyTests : TestBase
{
    [Fact]
    public void should_return_fixed_interval()
    {
        // given
        var interval = TimeSpan.FromSeconds(5);
        var strategy = new FixedIntervalBackoffStrategy(interval);

        // when
        var delay0 = strategy.GetNextDelay(0);
        var delay1 = strategy.GetNextDelay(1);
        var delay5 = strategy.GetNextDelay(5);
        var delay100 = strategy.GetNextDelay(100);

        // then - all delays should be exactly the same
        delay0.Should().Be(interval);
        delay1.Should().Be(interval);
        delay5.Should().Be(interval);
        delay100.Should().Be(interval);
    }

    [Fact]
    public void should_return_fixed_interval_for_transient_exceptions()
    {
        // given
        var interval = TimeSpan.FromSeconds(3);
        var strategy = new FixedIntervalBackoffStrategy(interval);

        // when
        var delayWithTransient = strategy.GetNextDelay(0, new TimeoutException());
        var delayWithNull = strategy.GetNextDelay(0);

        // then - fixed interval for transient exceptions and null
        delayWithTransient.Should().Be(interval);
        delayWithNull.Should().Be(interval);
    }

    [Fact]
    public void should_return_null_for_permanent_exceptions()
    {
        // given
        var strategy = new FixedIntervalBackoffStrategy(TimeSpan.FromSeconds(1));

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
        var strategy = new FixedIntervalBackoffStrategy(TimeSpan.FromSeconds(1));

        // when/then - transient exceptions should be retried
        strategy.GetNextDelay(0, new TimeoutException("Timeout")).Should().NotBeNull();
        strategy.GetNextDelay(0, new IOException("Network error")).Should().NotBeNull();
        strategy.GetNextDelay(0, new ApplicationException("Generic error")).Should().NotBeNull();
    }

    [Fact]
    public void should_reject_permanent_exceptions_via_should_retry()
    {
        // given
        var strategy = new FixedIntervalBackoffStrategy(TimeSpan.FromSeconds(1));

        // when/then - permanent exceptions should not be retried
        strategy.ShouldRetry(new SubscriberNotFoundException("Not found")).Should().BeFalse();
        strategy.ShouldRetry(new ArgumentNullException("value")).Should().BeFalse();
        strategy.ShouldRetry(new ArgumentException("Invalid", "value")).Should().BeFalse();
        strategy.ShouldRetry(new InvalidOperationException("Op")).Should().BeFalse();
        strategy.ShouldRetry(new NotSupportedException("Not")).Should().BeFalse();

        // transient exceptions should be retried
        strategy.ShouldRetry(new TimeoutException("Timeout")).Should().BeTrue();
        strategy.ShouldRetry(new IOException("Network")).Should().BeTrue();
        strategy.ShouldRetry(new ApplicationException("Generic")).Should().BeTrue();
    }

    [Fact]
    public void should_handle_zero_interval()
    {
        // given
        var strategy = new FixedIntervalBackoffStrategy(TimeSpan.Zero);

        // when
        var delay = strategy.GetNextDelay(0);

        // then
        delay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void should_handle_small_interval()
    {
        // given
        var interval = TimeSpan.FromMilliseconds(1);
        var strategy = new FixedIntervalBackoffStrategy(interval);

        // when
        var delay = strategy.GetNextDelay(0);

        // then
        delay.Should().Be(interval);
    }

    [Fact]
    public void should_handle_large_interval()
    {
        // given
        var interval = TimeSpan.FromHours(1);
        var strategy = new FixedIntervalBackoffStrategy(interval);

        // when
        var delay = strategy.GetNextDelay(0);

        // then
        delay.Should().Be(interval);
    }

    [Fact]
    public void should_be_thread_safe()
    {
        // given
        var interval = TimeSpan.FromSeconds(2);
        var strategy = new FixedIntervalBackoffStrategy(interval);
        var results = new System.Collections.Concurrent.ConcurrentBag<TimeSpan?>();

        // when
        Parallel.For(
            0,
            1000,
            i =>
            {
                var delay = strategy.GetNextDelay(i % 10);
                results.Add(delay);
            }
        );

        // then - all results should be the same fixed interval
        results.Should().HaveCount(1000);
        results.Should().AllSatisfy(r => r.Should().Be(interval));
    }

    [Fact]
    public void should_never_return_null()
    {
        // given
        var strategy = new FixedIntervalBackoffStrategy(TimeSpan.FromSeconds(1));

        // when
        var delayWithException = strategy.GetNextDelay(0, new ApplicationException());
        var delayWithoutException = strategy.GetNextDelay(0);

        // then
        delayWithException.Should().NotBeNull();
        delayWithoutException.Should().NotBeNull();
    }
}
