// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class StringLengthTests
{
    [Fact]
    public void has_length_should_return_value_when_exact()
    {
        Argument.HasLength("abc", 3).Should().Be("abc");
    }

    [Fact]
    public void has_length_should_throw_when_not_exact()
    {
        var value = "abc";
        var action = () => Argument.HasLength(value, 4);

        action
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage("The argument \"value\" must have a length of 4 (Actual length 3). (Parameter 'value')");
    }

    [Fact]
    public void has_length_should_throw_argument_null_when_null()
    {
        var action = () => Argument.HasLength(null, 3);
        action.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void has_min_length_should_return_value_when_long_enough()
    {
        Argument.HasMinLength("abc", 2).Should().Be("abc");
        Argument.HasMinLength("abc", 3).Should().Be("abc");
    }

    [Fact]
    public void has_min_length_should_throw_when_too_short()
    {
        var value = "a";
        var action = () => Argument.HasMinLength(value, 2);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>().WithMessage("*at least 2*Actual length 1*");
    }

    [Fact]
    public void has_max_length_should_return_value_when_short_enough()
    {
        Argument.HasMaxLength("abc", 3).Should().Be("abc");
        Argument.HasMaxLength("abc", 5).Should().Be("abc");
    }

    [Fact]
    public void has_max_length_should_throw_when_too_long()
    {
        var value = "abcd";
        var action = () => Argument.HasMaxLength(value, 3);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>().WithMessage("*at most 3*Actual length 4*");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]
    public void has_length_between_should_return_value_when_in_range(string value)
    {
        Argument.HasLengthBetween(value, 2, 4).Should().Be(value);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("abcde")]
    public void has_length_between_should_throw_when_out_of_range(string value)
    {
        var action = () => Argument.HasLengthBetween(value, 2, 4);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void has_length_between_should_throw_when_bounds_inverted()
    {
        var action = () => Argument.HasLengthBetween("abc", 5, 2);
        action.Should().ThrowExactly<ArgumentException>();
    }
}
