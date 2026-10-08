// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests;

public sealed class TransactionLockBudgetTests : TestBase
{
    [Fact]
    public async Task should_pass_a_one_attempt_budget_through_to_every_resource()
    {
        var budget = TransactionLockBudget.Start(TimeSpan.Zero);
        await Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken);

        budget.TryGetRemaining(out var wait).Should().BeTrue();
        wait.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task should_pass_an_unbounded_budget_through_to_every_resource()
    {
        var budget = TransactionLockBudget.Start(Timeout.InfiniteTimeSpan);
        await Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken);

        budget.TryGetRemaining(out var wait).Should().BeTrue();
        wait.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public async Task should_give_the_next_resource_only_what_the_earlier_ones_left()
    {
        // given
        var total = TimeSpan.FromSeconds(30);
        var budget = TransactionLockBudget.Start(total);

        // when
        await Task.Delay(TimeSpan.FromMilliseconds(100), AbortToken);

        // then
        budget.TryGetRemaining(out var wait).Should().BeTrue();
        wait.Should().BeLessThanOrEqualTo(total - TimeSpan.FromMilliseconds(100));
        wait.Should().BePositive();
    }

    [Fact]
    public async Task should_report_a_bounded_budget_as_spent_once_it_elapses()
    {
        // given
        var budget = TransactionLockBudget.Start(TimeSpan.FromMilliseconds(50));

        // when
        await Task.Delay(TimeSpan.FromMilliseconds(150), AbortToken);

        // then
        budget.TryGetRemaining(out var wait).Should().BeFalse();
        wait.Should().BeLessThanOrEqualTo(TimeSpan.Zero);
    }
}
