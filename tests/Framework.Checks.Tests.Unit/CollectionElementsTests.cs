// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public sealed class CollectionElementsTests
{
    [Fact]
    public void has_no_nulls_throws_exception_if_argument_contains_null()
    {
        // given
        IReadOnlyCollection<string?> argument = ["value1", null, "value3"];
        const string customMessage = "The collection must not contains null elements.";

        // when
        Action action = () => Argument.HasNoNulls(argument);
        Action actionWithCustomMessage = () => Argument.HasNoNulls(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"argument\" cannot contains null elements. (Parameter 'argument')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'argument')");
    }

    [Fact]
    public void has_no_nulls_returns_argument_if_no_null_elements()
    {
        // given
        IReadOnlyCollection<string?> argument = ["value1", "value2", "value3"];

        // when
        var result = Argument.HasNoNulls(argument);

        // then
        result.Should().Equal(argument);
    }

    [Fact]
    public void has_no_null_or_empty_elements_throws_exception_if_argument_contains_empty_element()
    {
        // given
        IReadOnlyCollection<string?> argument = ["value1", "", "value3"];
        const string customMessage = "The collection must not contains empty elements.";

        // when
        Action action = () => Argument.HasNoNullOrEmptyElements(argument);
        Action actionWithCustomMessage = () => Argument.HasNoNullOrEmptyElements(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"argument\" cannot contains empty elements. (Parameter 'argument')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'argument')");
    }

    [Fact]
    public void has_no_null_or_empty_elements_returns_argument_if_no_empty_elements()
    {
        // given
        IReadOnlyCollection<string?> argument = new List<string?> { "value1", "value2", "value3" };

        // when
        var result = Argument.HasNoNullOrEmptyElements(argument);

        // then
        result.Should().Equal(argument);
    }

    [Fact]
    public void has_no_null_or_white_space_elements_throws_exception_if_argument_contains_white_space()
    {
        // given
        IReadOnlyCollection<string?> argument = new List<string?> { "value1", " ", "value3" };
        const string customMessage = "The collection must not contains white space elements.";

        // when
        Action action = () => Argument.HasNoNullOrWhiteSpaceElements(argument);
        Action actionWithCustomMessage = () => Argument.HasNoNullOrWhiteSpaceElements(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage(
                "The argument \"argument\" cannot contains empty or white space elements. (Parameter 'argument')"
            );

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'argument')");
    }

    [Fact]
    public void has_no_null_or_white_space_elements_returns_argument_if_no_white_space_elements()
    {
        // given
        IReadOnlyCollection<string?> argument = new List<string?> { "value1", "value2", "value3" };

        // when
        var result = Argument.HasNoNullOrWhiteSpaceElements(argument);

        // then
        result.Should().Equal(argument);
    }

    [Fact]
    public void has_no_null_or_white_space_elements_throws_exception_if_argument_contains_null()
    {
        // given
        IReadOnlyCollection<string?> argument = new List<string?> { "value1", null, "value3" };
        const string customMessage = "The collection must not contains null elements";
        // when
        Action action = () => Argument.HasNoNullOrWhiteSpaceElements(argument);
        Action actionWithCustomMessage = () => Argument.HasNoNullOrWhiteSpaceElements(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage(
                "The argument \"argument\" cannot contains empty or white space elements. (Parameter 'argument')"
            );

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'argument')");
    }
}
