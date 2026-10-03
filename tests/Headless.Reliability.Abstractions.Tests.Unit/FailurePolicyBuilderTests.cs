// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reliability;
using Headless.Testing.Tests;

namespace Tests;

public sealed class FailurePolicyBuilderTests : TestBase
{
    [Fact]
    public void should_build_a_policy_type_into_its_declared_definition()
    {
        // when
        var definition = new PaymentsPolicy().Build();

        // then
        definition.ImmediateRetries.Should().Be(2);
        definition.DelayedRetries.Should().Be(5);
        definition.DelayedInitialDelay.Should().Be(TimeSpan.FromSeconds(30));
        definition.DelayedMaxDelay.Should().Be(TimeSpan.FromMinutes(15));
        definition.FailOnExceptionTypes.Should().Equal(typeof(ArgumentException));
        definition.FailWhenRuleCount.Should().Be(1);
        definition.ShouldFail(new TimeoutException("card declined")).Should().BeTrue();
    }

    [Fact]
    public void should_build_equivalent_definitions_from_one_policy_type()
    {
        // when
        var first = new PaymentsPolicy().Build();
        var second = new PaymentsPolicy().Build();

        // then
        second.Should().NotBeSameAs(first);
        second.ImmediateRetries.Should().Be(first.ImmediateRetries);
        second.DelayedRetries.Should().Be(first.DelayedRetries);
        second.DelayedInitialDelay.Should().Be(first.DelayedInitialDelay);
        second.DelayedMaxDelay.Should().Be(first.DelayedMaxDelay);
        second.FailOnExceptionTypes.Should().Equal(first.FailOnExceptionTypes);
        second.FailWhenRuleCount.Should().Be(first.FailWhenRuleCount);
    }

    [Fact]
    public void should_build_no_retries_and_no_rules_from_an_empty_builder()
    {
        // when
        var definition = new FailurePolicyBuilder().Build();

        // then
        definition.TotalAttempts.Should().Be(1);
        definition.ShouldFail(new InvalidOperationException()).Should().BeFalse();
    }

    [Fact]
    public void should_replace_a_tier_that_is_configured_twice()
    {
        // when
        var definition = new FailurePolicyBuilder()
            .Immediate(3)
            .Immediate(1)
            .Delayed(5, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15))
            .Delayed(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4))
            .Build();

        // then
        definition.ImmediateRetries.Should().Be(1);
        definition.DelayedRetries.Should().Be(2);
        definition.DelayedInitialDelay.Should().Be(TimeSpan.FromSeconds(1));
        definition.DelayedMaxDelay.Should().Be(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void should_accumulate_fail_rules_and_ignore_a_repeated_type()
    {
        // when
        var definition = new FailurePolicyBuilder()
            .FailOn<ArgumentException>()
            .FailOn<TimeoutException>()
            .FailOn<ArgumentException>()
            .FailWhen(static _ => false)
            .FailWhen(static ex => ex is FormatException)
            .Build();

        // then
        definition.FailOnExceptionTypes.Should().Equal(typeof(ArgumentException), typeof(TimeoutException));
        definition.FailWhenRuleCount.Should().Be(2);
        definition.ShouldFail(new FormatException()).Should().BeTrue();
    }

    [Fact]
    public void should_not_change_a_built_definition_when_the_builder_keeps_changing()
    {
        // given
        var builder = new FailurePolicyBuilder().Immediate(1).FailOn<ArgumentException>();
        var definition = builder.Build();

        // when
        builder.Immediate(4).FailOn<TimeoutException>().FailWhen(static _ => true);

        // then
        definition.ImmediateRetries.Should().Be(1);
        definition.FailOnExceptionTypes.Should().Equal(typeof(ArgumentException));
        definition.FailWhenRuleCount.Should().Be(0);
        definition.ShouldFail(new TimeoutException()).Should().BeFalse();
    }

    [Fact]
    public void should_accept_the_boundary_values()
    {
        // when
        var definition = new FailurePolicyBuilder()
            .Immediate(FailurePolicyDefinition.MaxRetriesPerTier)
            .Delayed(
                FailurePolicyDefinition.MaxRetriesPerTier,
                FailurePolicyDefinition.MaxDelayLimit,
                FailurePolicyDefinition.MaxDelayLimit
            )
            .Build();

        // then
        definition.TotalAttempts.Should().Be(201);
    }

    [Fact]
    public void should_accept_zero_delays_when_there_are_no_delayed_retries()
    {
        // when
        var definition = new FailurePolicyBuilder().Delayed(0, TimeSpan.Zero, TimeSpan.Zero).Build();

        // then
        definition.DelayedRetries.Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void should_reject_immediate_retries_out_of_range(int retries)
    {
        // when
        var act = () => new FailurePolicyBuilder().Immediate(retries);

        // then
        act.Should().Throw<ArgumentException>().WithParameterName(nameof(retries));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void should_reject_delayed_retries_out_of_range(int retries)
    {
        // when
        var act = () => new FailurePolicyBuilder().Delayed(retries, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        // then
        act.Should().Throw<ArgumentException>().WithParameterName(nameof(retries));
    }

    [Fact]
    public void should_reject_zero_initial_delay_with_delayed_retries()
    {
        // when
        var act = () => new FailurePolicyBuilder().Delayed(1, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        // then
        act.Should().Throw<ArgumentException>().WithParameterName("initialDelay");
    }

    [Fact]
    public void should_reject_a_negative_initial_delay()
    {
        // when
        var act = () => new FailurePolicyBuilder().Delayed(0, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(2));

        // then
        act.Should().Throw<ArgumentException>().WithParameterName("initialDelay");
    }

    [Fact]
    public void should_reject_a_max_delay_below_the_initial_delay()
    {
        // when
        var act = () => new FailurePolicyBuilder().Delayed(1, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));

        // then
        act.Should().Throw<ArgumentException>().WithParameterName("maxDelay");
    }

    [Fact]
    public void should_reject_delays_above_the_limit()
    {
        // when
        var initialTooLong = () =>
            new FailurePolicyBuilder().Delayed(1, TimeSpan.FromHours(25), TimeSpan.FromHours(25));
        var maxTooLong = () => new FailurePolicyBuilder().Delayed(1, TimeSpan.FromHours(1), TimeSpan.FromHours(25));

        // then
        initialTooLong.Should().Throw<ArgumentException>().WithParameterName("initialDelay");
        maxTooLong.Should().Throw<ArgumentException>().WithParameterName("maxDelay");
    }

    [Fact]
    public void should_reject_a_null_predicate()
    {
        // when
        var act = () => new FailurePolicyBuilder().FailWhen(null!);

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
    }

    [Fact]
    public void should_surface_an_invalid_policy_type_when_it_is_built()
    {
        // when
        var act = () => new InvalidPolicy().Build();

        // then
        act.Should().Throw<ArgumentException>();
    }

    private sealed class PaymentsPolicy : FailurePolicy
    {
        protected override void Configure(FailurePolicyBuilder policy) =>
            policy
                .Immediate(retries: 2)
                .Delayed(retries: 5, initialDelay: TimeSpan.FromSeconds(30), maxDelay: TimeSpan.FromMinutes(15))
                .FailOn<ArgumentException>()
                .FailWhen(static ex => ex.Message.Contains("declined", StringComparison.Ordinal));
    }

    private sealed class InvalidPolicy : FailurePolicy
    {
        protected override void Configure(FailurePolicyBuilder policy) => policy.Immediate(-1);
    }
}
