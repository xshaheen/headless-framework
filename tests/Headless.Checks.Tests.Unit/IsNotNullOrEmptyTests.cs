// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class IsNotNullOrEmptyTests
{
    [Fact]
    public void is_not_null_or_empty_throws_for_null_string_or_empty_string()
    {
        // given
        const string? argumentNull = null;
        var nullCustomMessage = $"Error {argumentNull} is null";

        // when
        var nullAction = () => Argument.IsNotNullOrEmpty(argumentNull);
        var nullActionWithCustomMessage = () => Argument.IsNotNullOrEmpty(argumentNull, nullCustomMessage);

        // then
        nullAction
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage("Required argument \"argumentNull\" was null. (Parameter 'argumentNull')");

        nullActionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage($"{nullCustomMessage} (Parameter 'argumentNull')");

        // given
        const string argumentEmpty = "";
        var emptyCustomMessage = $"Error {argumentEmpty} is empty";

        // when
        var emptyAction = () => Argument.IsNotNullOrEmpty(argumentEmpty);
        var emptyActionWithCustomMessage = () => Argument.IsNotNullOrEmpty(argumentEmpty, emptyCustomMessage);

        // then
        emptyAction
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("Required argument \"argumentEmpty\" was empty. (Parameter 'argumentEmpty')");

        emptyActionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{emptyCustomMessage} (Parameter 'argumentEmpty')");
    }

    [Fact]
    public void is_not_null_or_empty_returns_string_for_valid_input()
    {
        // given
        const string input = "valid input";

        // when & then
        Argument.IsNotNullOrEmpty(input).Should().Be(input);
    }

    [Fact]
    public void is_not_null_or_empty_throws_for_null_or_empty_readonly_collection()
    {
        // given
        IReadOnlyCollection<int>? argumentNull = null;
        const string nullCustomMessage = "Error collection is null";

        // when
        Action nullAction = () => Argument.IsNotNullOrEmpty(argumentNull);
        Action nullActionWithCustomMessage = () => Argument.IsNotNullOrEmpty(argumentNull, nullCustomMessage);

        // then
        nullAction
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage("Required argument \"argumentNull\" was null. (Parameter 'argumentNull')");

        nullActionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage($"{nullCustomMessage} (Parameter 'argumentNull')");

        // given
        IReadOnlyCollection<int> argumentEmpty = new List<int>();
        const string emptyCustomMessage = "Error collection is empty";

        // when
        Action action = () => Argument.IsNotNullOrEmpty(argumentEmpty);
        Action actionWithCustomMessage = () => Argument.IsNotNullOrEmpty(argumentEmpty, emptyCustomMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("Required argument \"argumentEmpty\" was empty. (Parameter 'argumentEmpty')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{emptyCustomMessage} (Parameter 'argumentEmpty')");
    }

    [Fact]
    public void is_not_null_or_empty_returns_readonly_collection_for_valid_input()
    {
        // given
        IReadOnlyCollection<int> input = new List<int> { 1, 2, 3 };

        // when & then
        Argument.IsNotNullOrEmpty(input).Should().Equal(input);
    }

    [Fact]
    public void is_not_null_or_empty_throws_for_empty_or_null_enumerable()
    {
        // given
        IEnumerable<int>? argumentNull = null;
        const string nullCustomMessage = "Error elements are null";

        // when
        Action nullAction = () => Argument.IsNotNullOrEmpty(argumentNull);
        Action nullActionWithCustomMessage = () => Argument.IsNotNullOrEmpty(argumentNull, nullCustomMessage);

        // then
        nullAction
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage("Required argument \"argumentNull\" was null. (Parameter 'argumentNull')");

        nullActionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage($"{nullCustomMessage} (Parameter 'argumentNull')");

        // given
        IEnumerable<int> argumentEmpty = new List<int>();
        const string emptyCustomMessage = "Error elements are empty";

        // when
        Action emptyAction = () => Argument.IsNotNullOrEmpty(argumentEmpty);
        Action emptyActionWithCustomMessage = () => Argument.IsNotNullOrEmpty(argumentEmpty, emptyCustomMessage);

        // then
        emptyAction
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("Required argument \"argumentEmpty\" was empty. (Parameter 'argumentEmpty')");

        emptyActionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{emptyCustomMessage} (Parameter 'argumentEmpty')");
    }

    [Fact]
    public void is_not_null_or_empty_returns_enumerable_for_valid_input()
    {
        // given
        IEnumerable<int> input = new List<int> { 1, 2, 3 };

        // when & then
        Argument.IsNotNullOrEmpty(input).Should().Equal(input);
    }

    [Fact]
    public void should_accept_whitespace_only_string()
    {
        // given - whitespace is NOT empty (use IsNotNullOrWhiteSpace for that)
        const string whitespace = "   ";

        // when
        var result = Argument.IsNotNullOrEmpty(whitespace);

        // then
        result.Should().Be(whitespace);
    }
}
