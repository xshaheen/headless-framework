// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reliability;
using Headless.Testing.Tests;

namespace Tests;

public sealed class FailurePolicyDefinitionTests : TestBase
{
    private static readonly TimeSpan _Initial = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _Max = TimeSpan.FromMinutes(15);

    [Fact]
    public void should_count_first_attempt_plus_both_retry_tiers()
    {
        // given
        var definition = new FailurePolicyBuilder().Immediate(2).Delayed(5, _Initial, _Max).Build();

        // when
        var total = definition.TotalAttempts;

        // then
        total.Should().Be(8);
    }

    [Fact]
    public void should_allow_a_single_attempt_when_policy_has_no_retries()
    {
        // when
        var definition = FailurePolicyDefinition.None;

        // then
        definition.TotalAttempts.Should().Be(1);
        definition.ImmediateRetries.Should().Be(0);
        definition.DelayedRetries.Should().Be(0);
        definition.FailOnExceptionTypes.Should().BeEmpty();
        definition.FailWhenRuleCount.Should().Be(0);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    [InlineData(6, 900)]
    [InlineData(7, 900)]
    [InlineData(20, 900)]
    public void should_double_the_delay_per_delayed_attempt_up_to_the_cap(int attempt, int expectedSeconds)
    {
        // given
        var definition = new FailurePolicyBuilder().Delayed(5, _Initial, _Max).Build();

        // when
        var baseDelay = definition.GetDelayedRetryBaseDelay(attempt);
        var midpointDelay = definition.GetDelayedRetryDelay(attempt, new FixedRandom(0.5));

        // then
        baseDelay.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
        midpointDelay.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData(62)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(1_000)]
    [InlineData(int.MaxValue)]
    public void should_return_the_cap_without_overflow_for_large_attempts(int attempt)
    {
        // given
        var definition = new FailurePolicyBuilder()
            .Delayed(5, TimeSpan.FromSeconds(1), FailurePolicyDefinition.MaxDelayLimit)
            .Build();

        // when
        var baseDelay = definition.GetDelayedRetryBaseDelay(attempt);
        var jittered = definition.GetDelayedRetryDelay(attempt, new FixedRandom(0.999_999));

        // then
        baseDelay.Should().Be(FailurePolicyDefinition.MaxDelayLimit);
        jittered.Should().Be(FailurePolicyDefinition.MaxDelayLimit);
    }

    [Fact]
    public void should_return_the_cap_when_initial_delay_equals_the_cap()
    {
        // given
        var definition = new FailurePolicyBuilder().Delayed(3, _Max, _Max).Build();

        // when
        var delay = definition.GetDelayedRetryBaseDelay(1);

        // then
        delay.Should().Be(_Max);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_a_delayed_attempt_below_one(int attempt)
    {
        // given
        var definition = new FailurePolicyBuilder().Delayed(5, _Initial, _Max).Build();

        // when
        var baseAct = () => definition.GetDelayedRetryBaseDelay(attempt);
        var jitterAct = () => definition.GetDelayedRetryDelay(attempt);

        // then
        baseAct.Should().Throw<ArgumentOutOfRangeException>();
        jitterAct.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_apply_the_lower_jitter_edge_of_twenty_percent()
    {
        // given
        var definition = new FailurePolicyBuilder().Delayed(5, _Initial, _Max).Build();

        // when
        var delay = definition.GetDelayedRetryDelay(2, new FixedRandom(0));

        // then
        delay.Should().Be(TimeSpan.FromSeconds(48));
    }

    [Fact]
    public void should_keep_the_upper_jitter_edge_below_twenty_percent_above_the_base()
    {
        // given
        var definition = new FailurePolicyBuilder().Delayed(5, _Initial, _Max).Build();

        // when
        var delay = definition.GetDelayedRetryDelay(2, new FixedRandom(0.999_999));

        // then
        delay.Should().BeLessThan(TimeSpan.FromSeconds(72));
        delay.Should().BeGreaterThan(TimeSpan.FromSeconds(71.9));
    }

    [Fact]
    public void should_not_exceed_the_cap_when_jitter_pushes_above_it()
    {
        // given
        var definition = new FailurePolicyBuilder().Delayed(10, _Initial, _Max).Build();

        // when
        var delay = definition.GetDelayedRetryDelay(6, new FixedRandom(0.999_999));

        // then
        delay.Should().Be(_Max);
    }

    [Fact]
    public void should_keep_every_jittered_delay_within_the_band_and_under_the_cap()
    {
        // given
        var definition = new FailurePolicyBuilder().Delayed(10, _Initial, _Max).Build();
        var random = new Random(1049);

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var expectedBase = TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, attempt - 1), 900));
            var lower = expectedBase * 0.8;
            var upper = TimeSpan.FromTicks(Math.Min((expectedBase * 1.2).Ticks, _Max.Ticks));

            for (var i = 0; i < 200; i++)
            {
                // when
                var delay = definition.GetDelayedRetryDelay(attempt, random);

                // then
                delay.Should().BeGreaterThanOrEqualTo(lower);
                delay.Should().BeLessThanOrEqualTo(upper);
            }
        }
    }

    [Fact]
    public void should_use_a_shared_random_source_when_none_is_supplied()
    {
        // given
        var definition = new FailurePolicyBuilder().Delayed(3, _Initial, _Max).Build();

        // when
        var delay = definition.GetDelayedRetryDelay(1);

        // then
        delay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(24));
        delay.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(36));
    }

    [Fact]
    public void should_return_zero_delay_for_a_policy_without_delayed_retries()
    {
        // when
        var delay = FailurePolicyDefinition.None.GetDelayedRetryDelay(1, new FixedRandom(0.999_999));

        // then
        delay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void should_fail_on_a_declared_type_and_its_subtypes_only()
    {
        // given
        var definition = new FailurePolicyBuilder().FailOn<InvalidOperationException>().Build();

        // when / then
        definition.ShouldFail(new InvalidOperationException()).Should().BeTrue();
        definition.ShouldFail(new ObjectDisposedException("x")).Should().BeTrue();
        definition.ShouldFail(_ArgumentError("value")).Should().BeFalse();
        definition.ShouldFail(new Exception("base")).Should().BeFalse();
    }

    [Fact]
    public void should_fail_when_a_predicate_matches()
    {
        // given
        var definition = new FailurePolicyBuilder()
            .FailWhen(static ex => string.Equals(ex.Message, "permanent", StringComparison.Ordinal))
            .Build();

        // when / then
        definition.ShouldFail(new InvalidOperationException("permanent")).Should().BeTrue();
        definition.ShouldFail(new InvalidOperationException("transient")).Should().BeFalse();
    }

    [Fact]
    public void should_fail_and_report_the_exception_when_a_predicate_throws()
    {
        // given
        var thrown = new FormatException("broken rule");
        var definition = new FailurePolicyBuilder().FailWhen(_ => throw thrown).Build();

        // when
        var shouldFail = definition.ShouldFail(new TimeoutException(), out var ruleException);
        var shouldFailShort = definition.ShouldFail(new TimeoutException());

        // then
        shouldFail.Should().BeTrue();
        ruleException.Should().BeSameAs(thrown);
        shouldFailShort.Should().BeTrue();
    }

    [Fact]
    public void should_retry_an_exception_that_no_rule_matches()
    {
        // given
        var definition = new FailurePolicyBuilder()
            .FailOn<ArgumentException>()
            .FailWhen(static ex => ex is TimeoutException)
            .Build();

        // when
        var shouldFail = definition.ShouldFail(new InvalidOperationException(), out var ruleException);

        // then
        shouldFail.Should().BeFalse();
        ruleException.Should().BeNull();
    }

    [Fact]
    public void should_not_report_a_rule_exception_when_a_type_rule_matches()
    {
        // given
        var definition = new FailurePolicyBuilder().FailOn<ArgumentException>().Build();

        // when
        var shouldFail = definition.ShouldFail(_ArgumentNullError("value"), out var ruleException);

        // then
        shouldFail.Should().BeTrue();
        ruleException.Should().BeNull();
    }

    [Fact]
    public void should_reject_a_null_exception()
    {
        // when
        var act = () => FailurePolicyDefinition.None.ShouldFail(null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_replace_only_supplied_numeric_fields_and_keep_fail_rules()
    {
        // given
        var definition = new FailurePolicyBuilder()
            .Immediate(2)
            .Delayed(5, _Initial, _Max)
            .FailOn<ArgumentException>()
            .FailWhen(static ex => ex is TimeoutException)
            .Build();

        // when
        var overridden = definition.With(
            new FailurePolicyOverrides { DelayedRetries = 3, DelayedMaxDelay = TimeSpan.FromMinutes(5) }
        );

        // then
        overridden.ImmediateRetries.Should().Be(2);
        overridden.DelayedRetries.Should().Be(3);
        overridden.DelayedInitialDelay.Should().Be(_Initial);
        overridden.DelayedMaxDelay.Should().Be(TimeSpan.FromMinutes(5));
        overridden.TotalAttempts.Should().Be(6);
        overridden.FailOnExceptionTypes.Should().Equal(typeof(ArgumentException));
        overridden.FailWhenRuleCount.Should().Be(1);
        overridden.ShouldFail(_ArgumentNullError("value")).Should().BeTrue();
        overridden.ShouldFail(new TimeoutException()).Should().BeTrue();
        overridden.ShouldFail(new InvalidOperationException()).Should().BeFalse();
        definition.DelayedRetries.Should().Be(5, "the original definition must stay unchanged");
        definition.DelayedMaxDelay.Should().Be(_Max);
    }

    [Fact]
    public void should_replace_every_numeric_field_when_all_are_supplied()
    {
        // given
        var definition = new FailurePolicyBuilder().Immediate(2).Delayed(5, _Initial, _Max).Build();

        // when
        var overridden = definition.With(
            new FailurePolicyOverrides
            {
                ImmediateRetries = 0,
                DelayedRetries = 1,
                DelayedInitialDelay = TimeSpan.FromSeconds(5),
                DelayedMaxDelay = TimeSpan.FromSeconds(10),
            }
        );

        // then
        overridden.ImmediateRetries.Should().Be(0);
        overridden.DelayedRetries.Should().Be(1);
        overridden.DelayedInitialDelay.Should().Be(TimeSpan.FromSeconds(5));
        overridden.DelayedMaxDelay.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void should_return_the_same_definition_when_overrides_are_empty()
    {
        // given
        var definition = new FailurePolicyBuilder().Immediate(1).Build();

        // when
        var overridden = definition.With(new FailurePolicyOverrides());

        // then
        overridden.Should().BeSameAs(definition);
    }

    [Fact]
    public void should_reject_overrides_that_the_builder_would_reject()
    {
        // given
        var noDelays = new FailurePolicyBuilder().Immediate(1).Build();
        var delayed = new FailurePolicyBuilder().Delayed(5, _Initial, _Max).Build();

        // when
        var negativeRetries = () => delayed.With(new FailurePolicyOverrides { ImmediateRetries = -1 });
        var tooManyRetries = () => delayed.With(new FailurePolicyOverrides { DelayedRetries = 101 });
        var delayedWithoutInitial = () => noDelays.With(new FailurePolicyOverrides { DelayedRetries = 3 });
        var maxBelowInitial = () =>
            delayed.With(new FailurePolicyOverrides { DelayedMaxDelay = TimeSpan.FromSeconds(10) });
        var initialAboveLimit = () =>
            delayed.With(
                new FailurePolicyOverrides
                {
                    DelayedInitialDelay = TimeSpan.FromHours(25),
                    DelayedMaxDelay = TimeSpan.FromHours(25),
                }
            );

        // then
        negativeRetries.Should().Throw<ArgumentException>();
        tooManyRetries.Should().Throw<ArgumentException>();
        delayedWithoutInitial.Should().Throw<ArgumentException>();
        maxBelowInitial.Should().Throw<ArgumentException>();
        initialAboveLimit.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_null_overrides()
    {
        // when
        var act = () => FailurePolicyDefinition.None.With(null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_create_the_definition_a_policy_type_describes()
    {
        // when
        var definition = FailurePolicyDefinition.Create<OrdersPolicy>();

        // then
        definition.ImmediateRetries.Should().Be(1);
        definition.DelayedRetries.Should().Be(3);
        definition.DelayedInitialDelay.Should().Be(_Initial);
        definition.DelayedMaxDelay.Should().Be(_Max);
        definition.FailOnExceptionTypes.Should().Equal(typeof(InvalidOperationException));
    }

    [Fact]
    public void should_create_the_definition_an_inline_configuration_describes()
    {
        // when
        var definition = FailurePolicyDefinition.Create(p =>
            p.Immediate(2).Delayed(4, _Initial, _Max).FailWhen(static e => e is TimeoutException)
        );

        // then
        definition.ImmediateRetries.Should().Be(2);
        definition.DelayedRetries.Should().Be(4);
        definition.DelayedInitialDelay.Should().Be(_Initial);
        definition.DelayedMaxDelay.Should().Be(_Max);
        definition.FailWhenRuleCount.Should().Be(1);
        definition.ShouldFail(new TimeoutException()).Should().BeTrue();
    }

    [Fact]
    public void should_reject_a_null_configuration_when_creating_inline()
    {
        // when
        var act = () => FailurePolicyDefinition.Create(null!);

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void should_hold_no_mutable_state()
    {
        // given
        var type = typeof(FailurePolicyDefinition);
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;

        // when
        var mutableFields = type.GetFields(flags).Where(static f => !f.IsInitOnly).Select(static f => f.Name);
        var settableProperties = type.GetProperties(flags)
            .Where(static p => p.SetMethod is not null)
            .Select(static p => p.Name);

        // then
        type.IsSealed.Should().BeTrue();
        mutableFields.Should().BeEmpty();
        settableProperties.Should().BeEmpty();
    }

    private static ArgumentException _ArgumentError(string paramName) => new("Invalid argument.", paramName);

    private static ArgumentNullException _ArgumentNullError(string paramName) => new(paramName);

    private sealed class FixedRandom(double sample) : Random
    {
        public override double NextDouble() => sample;
    }

    private sealed class OrdersPolicy : FailurePolicy
    {
        protected override void Configure(FailurePolicyBuilder policy) =>
            policy.Immediate(1).Delayed(3, _Initial, _Max).FailOn<InvalidOperationException>();
    }
}
