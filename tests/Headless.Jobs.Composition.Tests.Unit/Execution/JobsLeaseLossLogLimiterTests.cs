// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Execution;

public sealed class JobsLeaseLossLogLimiterTests : TestBase
{
    private static readonly TimeSpan _Window = TimeSpan.FromMinutes(1);

    [Fact]
    public void should_log_the_first_loss_and_hold_back_the_rest_of_the_window_when_one_function_loses_many_leases()
    {
        // given
        var limiter = new JobsLeaseLossLogLimiter(new FakeTimeProvider(), _Window);

        // when
        var first = limiter.TryEnter("orders.sync", out var firstSuppressed);
        var rest = Enumerable.Range(0, 5).Select(_ => limiter.TryEnter("orders.sync", out _)).ToList();

        // then
        first.Should().BeTrue();
        firstSuppressed.Should().Be(0);
        rest.Should().AllSatisfy(x => x.Should().BeFalse());
    }

    [Fact]
    public void should_log_again_with_the_held_back_count_when_the_window_has_passed()
    {
        // given
        var time = new FakeTimeProvider();
        var limiter = new JobsLeaseLossLogLimiter(time, _Window);
        limiter.TryEnter("orders.sync", out _);
        limiter.TryEnter("orders.sync", out _);
        limiter.TryEnter("orders.sync", out _);

        // when
        time.Advance(_Window);
        var logged = limiter.TryEnter("orders.sync", out var suppressed);
        var next = limiter.TryEnter("orders.sync", out _);

        // then
        logged.Should().BeTrue();
        suppressed.Should().Be(2);
        next.Should().BeFalse("the warning just logged opened a new window");
    }

    [Fact]
    public void should_keep_one_window_per_function_when_two_functions_lose_leases()
    {
        // given
        var limiter = new JobsLeaseLossLogLimiter(new FakeTimeProvider(), _Window);
        limiter.TryEnter("orders.sync", out _);

        // when
        var other = limiter.TryEnter("invoices.send", out var suppressed);

        // then
        other.Should().BeTrue();
        suppressed.Should().Be(0);
    }
}
