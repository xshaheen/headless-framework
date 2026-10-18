// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests.Configuration;

public sealed class RetryPolicyLeaseRenewalOptionsTests : TestBase
{
    [Fact]
    public void should_renew_every_third_of_the_dispatch_timeout_by_default()
    {
        var options = new RetryPolicyOptions { DispatchTimeout = TimeSpan.FromMinutes(3) };

        options.LeaseRenewalInterval.Should().BeNull();
        options.ResolveLeaseRenewalInterval().Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void should_use_an_explicit_renewal_interval()
    {
        var options = new RetryPolicyOptions { LeaseRenewalInterval = TimeSpan.FromSeconds(20) };

        options.ResolveLeaseRenewalInterval().Should().Be(TimeSpan.FromSeconds(20));
        new RetryPolicyOptionsValidator().Validate(options).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(300)]
    [InlineData(301)]
    public void should_reject_a_renewal_interval_that_is_not_positive_or_not_below_the_dispatch_timeout(int seconds)
    {
        // A renewal at or after DispatchTimeout lands after the lease it renews has already ended.
        var options = new RetryPolicyOptions
        {
            DispatchTimeout = TimeSpan.FromMinutes(5),
            LeaseRenewalInterval = TimeSpan.FromSeconds(seconds),
        };

        var result = new RetryPolicyOptionsValidator().Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(RetryPolicyOptions.LeaseRenewalInterval));
    }

    [Fact]
    public void should_copy_the_renewal_interval()
    {
        var source = new MessagingOptions { RetryPolicy = { LeaseRenewalInterval = TimeSpan.FromSeconds(42) } };
        var target = new MessagingOptions();

        source.RetryPolicy.CopyTo(target.RetryPolicy);

        target.RetryPolicy.LeaseRenewalInterval.Should().Be(TimeSpan.FromSeconds(42));
    }
}
