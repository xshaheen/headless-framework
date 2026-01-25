// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable CA2201 // Do not raise reserved exception types - test code intentionally tests generic exception handling
#pragma warning disable MA0015 // Specify the parameter name in ArgumentException - test code is verifying exception type handling

using Framework.Testing.Tests;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Retry;

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
    public void should_return_fixed_interval_regardless_of_exception()
    {
        // given
        var interval = TimeSpan.FromSeconds(3);
        var strategy = new FixedIntervalBackoffStrategy(interval);

        // when
        var delayWithTransient = strategy.GetNextDelay(0, new TimeoutException());
        var delayWithPermanent = strategy.GetNextDelay(0, new SubscriberNotFoundException("Not found"));
        var delayWithNull = strategy.GetNextDelay(0);

        // then - fixed interval ignores exception type for delay calculation
        delayWithTransient.Should().Be(interval);
        delayWithPermanent.Should().Be(interval);
        delayWithNull.Should().Be(interval);
    }

    [Fact]
    public void should_always_retry_via_should_retry()
    {
        // given
        var strategy = new FixedIntervalBackoffStrategy(TimeSpan.FromSeconds(1));

        // when/then - always returns true (maintains legacy behavior)
        strategy.ShouldRetry(new SubscriberNotFoundException("Not found")).Should().BeTrue();
        strategy.ShouldRetry(new ArgumentNullException("value")).Should().BeTrue();
        strategy.ShouldRetry(new ArgumentException("Invalid", "value")).Should().BeTrue();
        strategy.ShouldRetry(new InvalidOperationException("Op")).Should().BeTrue();
        strategy.ShouldRetry(new NotSupportedException("Not")).Should().BeTrue();
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
        Parallel.For(0, 1000, i =>
        {
            var delay = strategy.GetNextDelay(i % 10);
            results.Add(delay);
        });

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
