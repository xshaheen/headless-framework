// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;

namespace Tests;

public sealed class JobsLeaseRenewalOptionsTests
{
    [Fact]
    public void resolve_lease_renewal_interval_defaults_to_one_third_of_lease_duration()
    {
        var options = new SchedulerOptionsBuilder { LeaseDuration = TimeSpan.FromMinutes(6) };

        // ≈ LeaseDuration / 3 so one missed renewal cannot lapse the lease (#316/U1).
        options.ResolveLeaseRenewalInterval().Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void resolve_lease_renewal_interval_uses_explicit_value_when_set_and_valid()
    {
        var options = new SchedulerOptionsBuilder
        {
            LeaseDuration = TimeSpan.FromMinutes(5),
            LeaseRenewalInterval = TimeSpan.FromSeconds(30),
        };

        options.ResolveLeaseRenewalInterval().Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void resolve_lease_renewal_interval_rejects_an_interval_at_or_above_lease_duration()
    {
        var options = new SchedulerOptionsBuilder
        {
            LeaseDuration = TimeSpan.FromMinutes(5),
            LeaseRenewalInterval = TimeSpan.FromMinutes(5),
        };

        var act = () => options.ResolveLeaseRenewalInterval();

        act.Should().Throw<InvalidOperationException>().WithMessage("*strictly less than*");
    }

    [Fact]
    public void resolve_lease_renewal_interval_rejects_a_non_positive_interval()
    {
        var options = new SchedulerOptionsBuilder
        {
            LeaseDuration = TimeSpan.FromMinutes(5),
            LeaseRenewalInterval = TimeSpan.Zero,
        };

        var act = () => options.ResolveLeaseRenewalInterval();

        act.Should().Throw<InvalidOperationException>().WithMessage("*positive*");
    }
}
