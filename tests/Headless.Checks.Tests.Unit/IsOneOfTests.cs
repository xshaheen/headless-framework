// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Checks;

namespace Tests;

public sealed class IsOneOfTests
{
    [Fact]
    public void is_one_of_int_should_return_argument_when_valid()
    {
        // given
        const int argument = 5;
        var validValues = new List<int> { 1, 2, 5, 7 };

        // when & then
        Argument.IsOneOf(argument, validValues).Should().Be(argument);
    }

    [Fact]
    public void is_one_of_int_should_throw_when_invalid()
    {
        // given
        const int argument = 6;
        const string customMessage = $"{nameof(argument)} is not one of below list.";

        // when
        var action = () =>
        {
            ReadOnlySpan<int> validValues = [1, 2, 5, 7];

            return Argument.IsOneOf(argument, validValues);
        };

        var actionWithCustomMessage = () =>
        {
            ReadOnlySpan<int> validValues = [1, 2, 5, 7];

            return Argument.IsOneOf(argument, validValues, customMessage);
        };

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage(
                $"The argument \"{nameof(argument)}\"=<{argument}> must be one of [1,2,5,7]. (Parameter 'argument')"
            );

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'argument')");
    }

    [Fact]
    public void is_one_of_long_should_return_argument_when_valid()
    {
        // given
        const long argument = 10L;
        var validValues = new List<long> { 5L, 10L, 20L };

        // when & then
        Argument.IsOneOf(argument, validValues).Should().Be(argument);
    }

    [Fact]
    public void is_one_of_decimal_should_return_argument_when_valid()
    {
        // given
        const decimal argument = 1.5m;
        var validValues = new List<decimal> { 1.0m, 1.5m, 2.0m };

        // when & then
        Argument.IsOneOf(argument, validValues).Should().Be(argument);
    }

    [Fact]
    public void is_one_of_float_should_return_argument_when_valid()
    {
        // given
        const float argument = 2.5f;
        var validValues = new List<float> { 1.0f, 2.5f, 3.0f };

        // when & then
        Argument.IsOneOf(argument, validValues).Should().Be(argument);
    }

    [Fact]
    public void is_one_of_float_should_throw_when_invalid()
    {
        // given
        const float argument = 4.5f;
        const string customMessage = $"{nameof(argument)} is not one of below list.";

        var validValues = new List<float> { 1.0f, 2.5f, 3.0f };

        // when
        var action = () => Argument.IsOneOf(argument, validValues);
        var actionWithCustomMessage = () => Argument.IsOneOf(argument, validValues, null, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"argument\"=<4.5> must be one of [1,2.5,3]. (Parameter 'argument')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'argument')");
    }

    [Fact]
    public void is_one_of_string_should_return_argument_when_valid()
    {
        // given
        const string argument = "apple";
        var validValues = new List<string> { "apple", "banana", "cherry" };

        // when & then
        Argument.IsOneOf(argument, validValues).Should().Be(argument);
    }

    [Fact]
    public void is_one_of_string_should_throw_when_invalid()
    {
        // given
        const string argument = "invalid";
        var values = new List<string> { "zad", "framework", "storm" };

        // when
        var listAction = () => Argument.IsOneOf(argument, values);
        var spanAction = () => Argument.IsOneOf(argument, CollectionsMarshal.AsSpan(values));

        // then
        const string message =
            "The argument \"argument\"=\"invalid\" must be one of [zad,framework,storm]. (Parameter 'argument')";

        listAction.Should().ThrowExactly<ArgumentException>().WithMessage(message);
        spanAction.Should().ThrowExactly<ArgumentException>().WithMessage(message);
    }

    [Fact]
    public void is_one_of_string_should_throw_when_invalid_and_use_only_first_5_valid_items_in_the_message()
    {
        // given
        const string argument = "invalid";
        List<string> values = ["1", "2", "3", "4", "5", "6", "7", "8", "9"];

        // when
        var listAction = () => Argument.IsOneOf(argument, values);
        var spanAction = () => Argument.IsOneOf(argument, CollectionsMarshal.AsSpan(values));

        // then
        const string message =
            "The argument \"argument\"=\"invalid\" must be one of [1,2,3,4,5,...]. (Parameter 'argument')";

        listAction.Should().ThrowExactly<ArgumentException>().WithMessage(message);
        spanAction.Should().ThrowExactly<ArgumentException>().WithMessage(message);
    }

    [Fact]
    public void is_one_of_string_should_respect_string_comparer()
    {
        // given
        const string argument = "framework";
        var validValues = new List<string> { "zad", "framework", "storm" };

        // when & then
        Argument.IsOneOf(argument, validValues, StringComparer.OrdinalIgnoreCase);
    }
}
