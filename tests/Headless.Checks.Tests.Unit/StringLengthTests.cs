// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class StringLengthTests
{
    [Fact]
    public void should_return_value_when_has_length_exact()
    {
        Argument.HasLength("abc", 3).Should().Be("abc");
    }

    [Fact]
    public void should_throw_when_has_length_not_exact()
    {
        const string value = "abc";
        var action = () => Argument.HasLength(value, 4);

        action
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage("The argument \"value\" must have a length of 4 (Actual length 3). (Parameter 'value')");
    }

    [Fact]
    public void should_throw_argument_null_when_has_length_null()
    {
        var action = () => Argument.HasLength(null, 3);
        action.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void should_return_value_when_has_min_length_long_enough()
    {
        Argument.HasMinLength("abc", 2).Should().Be("abc");
        Argument.HasMinLength("abc", 3).Should().Be("abc");
    }

    [Fact]
    public void should_throw_when_has_min_length_too_short()
    {
        const string value = "a";
        var action = () => Argument.HasMinLength(value, 2);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>().WithMessage("*at least 2*Actual length 1*");
    }

    [Fact]
    public void should_return_value_when_has_max_length_short_enough()
    {
        Argument.HasMaxLength("abc", 3).Should().Be("abc");
        Argument.HasMaxLength("abc", 5).Should().Be("abc");
    }

    [Fact]
    public void should_throw_when_has_max_length_too_long()
    {
        const string value = "abcd";
        var action = () => Argument.HasMaxLength(value, 3);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>().WithMessage("*at most 3*Actual length 4*");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]
    public void should_return_value_when_has_length_between_in_range(string value)
    {
        Argument.HasLengthBetween(value, 2, 4).Should().Be(value);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("abcde")]
    public void should_throw_when_has_length_between_out_of_range(string value)
    {
        var action = () => Argument.HasLengthBetween(value, 2, 4);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_throw_when_has_length_between_bounds_inverted()
    {
        var action = () => Argument.HasLengthBetween("abc", 5, 2);
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void should_validate_exclusive_lower_bound_when_has_length_greater_than()
    {
        Argument.HasLengthGreaterThan("abcd", 3).Should().Be("abcd");

        const string value = "abc";
        var equalAction = () => Argument.HasLengthGreaterThan(value, 3);
        equalAction
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage(
                "The argument \"value\" must have a length greater than 3 (Actual length 3). (Parameter 'value')"
            );

        var shorterAction = () => Argument.HasLengthGreaterThan("ab", 3);
        shorterAction.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_validate_exclusive_upper_bound_when_has_length_less_than()
    {
        Argument.HasLengthLessThan("ab", 3).Should().Be("ab");

        var equalAction = () => Argument.HasLengthLessThan("abc", 3);
        equalAction.Should().ThrowExactly<ArgumentOutOfRangeException>().WithMessage("*less than 3*Actual length 3*");

        var longerAction = () => Argument.HasLengthLessThan("abcd", 3);
        longerAction.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_reject_exact_length_when_has_length_not_equal_to()
    {
        Argument.HasLengthNotEqualTo("abcd", 3).Should().Be("abcd");
        Argument.HasLengthNotEqualTo("ab", 3).Should().Be("ab");

        var action = () => Argument.HasLengthNotEqualTo("abc", 3);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>().WithMessage("*must not have a length of 3*");
    }

    [Fact]
    public void should_throw_argument_null_when_strict_length_guards_null()
    {
        ((Action)(() => Argument.HasLengthGreaterThan(null, 1))).Should().ThrowExactly<ArgumentNullException>();
        ((Action)(() => Argument.HasLengthLessThan(null, 1))).Should().ThrowExactly<ArgumentNullException>();
        ((Action)(() => Argument.HasLengthNotEqualTo(null, 1))).Should().ThrowExactly<ArgumentNullException>();
    }
}
