// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class IsCloseToTests
{
    [Theory]
    [InlineData(5, 7, 3)]
    [InlineData(5, 5, 0)]
    [InlineData(10, 8, 2)]
    public void is_close_to_should_return_value_when_within_delta(int value, int target, int delta)
    {
        Argument.IsCloseTo(value, target, delta).Should().Be(value);
    }

    [Fact]
    public void is_close_to_should_work_for_floating_point()
    {
        Argument.IsCloseTo(0.1 + 0.2, 0.3, 1e-9).Should().BeApproximately(0.3, 1e-9);
        Argument.IsCloseTo(1.0f, 1.05f, 0.1f).Should().Be(1.0f);
    }

    [Fact]
    public void is_close_to_should_throw_when_outside_delta()
    {
        var value = 5;
        var action = () => Argument.IsCloseTo(value, 10, 2);

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"value\" = 5 must be within 2 of 10. (Parameter 'value')");
    }

    [Fact]
    public void is_close_to_should_treat_nan_as_not_close()
    {
        var action = () => Argument.IsCloseTo(double.NaN, 1.0, 0.5);
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Theory]
    [InlineData(5, 10, 2)]
    [InlineData(0, 100, 50)]
    public void is_not_close_to_should_return_value_when_outside_delta(int value, int target, int delta)
    {
        Argument.IsNotCloseTo(value, target, delta).Should().Be(value);
    }

    [Fact]
    public void is_not_close_to_should_throw_when_within_delta()
    {
        var value = 5;
        var action = () => Argument.IsNotCloseTo(value, 6, 2);

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"value\" = 5 must not be within 2 of 6. (Parameter 'value')");
    }

    [Fact]
    public void is_not_close_to_should_treat_nan_as_not_close()
    {
        Argument.IsNotCloseTo(double.NaN, 1.0, 0.5).Should().Be(double.NaN);
    }

    [Fact]
    public void is_close_to_should_not_false_positive_at_integer_extremes()
    {
        // The true distance (~4.29e9) overflows int's signed range; it must NOT be reported as "close" to a small delta.
        var closeAction = () => Argument.IsCloseTo(int.MaxValue, int.MinValue, 5);
        closeAction.Should().ThrowExactly<ArgumentException>();

        Argument.IsNotCloseTo(int.MaxValue, int.MinValue, 5).Should().Be(int.MaxValue);
    }

    [Fact]
    public void is_close_to_unsigned_delta_can_express_full_int_distance()
    {
        // The full int span distance is uint.MaxValue (4_294_967_295) — not expressible with a signed int delta.
        Argument.IsCloseTo(int.MaxValue, int.MinValue, uint.MaxValue).Should().Be(int.MaxValue);

        // A delta below the true distance still rejects it (and stays overflow-safe).
        var action = () => Argument.IsCloseTo(int.MaxValue, int.MinValue, 3_000_000_000u);
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void is_close_to_unsigned_delta_overloads_match_message_format()
    {
        var value = 5;
        var action = () => Argument.IsCloseTo(value, 10, 2u);

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"value\" = 5 must be within 2 of 10. (Parameter 'value')");
    }

    [Fact]
    public void is_close_to_unsigned_delta_works_for_long()
    {
        Argument.IsCloseTo(100L, 90L, 20UL).Should().Be(100L);

        var action = () => Argument.IsCloseTo(100L, 50L, 10UL);
        action.Should().ThrowExactly<ArgumentException>();

        Argument.IsNotCloseTo(100L, 50L, 10UL).Should().Be(100L);
    }
}
