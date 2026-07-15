// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class IsZeroTests
{
    [Fact]
    public void should_return_value_when_is_zero_zero()
    {
        Argument.IsZero(0).Should().Be(0);
        Argument.IsZero(0d).Should().Be(0d);
        Argument.IsZero(0m).Should().Be(0m);
        Argument.IsZero(TimeSpan.Zero).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void should_throw_when_is_zero_not_zero()
    {
        const int value = 5;
        var action = () => Argument.IsZero(value);

        action
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage("The argument \"value\" must be zero. (Parameter 'value')");
    }

    [Fact]
    public void should_throw_for_nonzero_timespan_when_is_zero()
    {
        var action = () => Argument.IsZero(TimeSpan.FromSeconds(1));
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_pass_through_null_when_is_zero_nullable()
    {
        Argument.IsZero((int?)null).Should().BeNull();
        Argument.IsZero(null).Should().BeNull();
        Argument.IsZero((int?)0).Should().Be(0);

        var action = () => Argument.IsZero((int?)3);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_return_value_when_is_not_zero_not_zero()
    {
        Argument.IsNotZero(5).Should().Be(5);
        Argument.IsNotZero(TimeSpan.FromSeconds(1)).Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void should_throw_when_is_not_zero_zero()
    {
        const int value = 0;
        var action = () => Argument.IsNotZero(value);

        action
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage("The argument \"value\" must not be zero. (Parameter 'value')");

        var tsAction = () => Argument.IsNotZero(TimeSpan.Zero);
        tsAction.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_pass_through_null_when_is_not_zero_nullable()
    {
        Argument.IsNotZero((int?)null).Should().BeNull();
        Argument.IsNotZero(null).Should().BeNull();
        Argument.IsNotZero((int?)3).Should().Be(3);

        var action = () => Argument.IsNotZero((int?)0);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }
}
