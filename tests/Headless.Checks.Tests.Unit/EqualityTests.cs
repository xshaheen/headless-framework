// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class EqualityTests
{
    [Fact]
    public void should_return_value_when_is_equal_to_equal()
    {
        Argument.IsEqualTo(5, 5).Should().Be(5);
        Argument.IsEqualTo("abc", "abc").Should().Be("abc");
    }

    [Fact]
    public void should_throw_when_is_equal_to_not_equal()
    {
        // given
        const int value = 5;
        var action = () => Argument.IsEqualTo(value, 6);
        const string customMessage = "values differ";
        var actionWithCustomMessage = () => Argument.IsEqualTo(value, 6, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"value\" must be equal to <6>. (Parameter 'value')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'value')");
    }

    [Fact]
    public void should_use_supplied_comparer_when_is_equal_to()
    {
        Argument.IsEqualTo("ABC", "abc", StringComparer.OrdinalIgnoreCase).Should().Be("ABC");

        var action = () => Argument.IsEqualTo("ABC", "abd", StringComparer.OrdinalIgnoreCase);
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void should_return_value_when_is_not_equal_to_not_equal()
    {
        Argument.IsNotEqualTo(5, 6).Should().Be(5);
    }

    [Fact]
    public void should_throw_when_is_not_equal_to_equal()
    {
        // given
        const int value = 5;
        var action = () => Argument.IsNotEqualTo(value, 5);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"value\" must not be equal to <5>. (Parameter 'value')");
    }

    [Fact]
    public void should_use_supplied_comparer_when_is_not_equal_to()
    {
        var action = () => Argument.IsNotEqualTo("ABC", "abc", StringComparer.OrdinalIgnoreCase);
        action.Should().ThrowExactly<ArgumentException>();

        Argument.IsNotEqualTo("ABC", "xyz", StringComparer.OrdinalIgnoreCase).Should().Be("ABC");
    }

    [Fact]
    public void should_throw_argument_null_when_equality_with_null_comparer()
    {
        var action = () => Argument.IsEqualTo(1, 1, comparer: null!);
        action.Should().ThrowExactly<ArgumentNullException>();
    }
}
