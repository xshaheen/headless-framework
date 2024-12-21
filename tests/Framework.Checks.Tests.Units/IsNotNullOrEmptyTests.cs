// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class IsNotNullOrEmptyTests
{
    [Fact]
    public void is_not_null_or_empty_throws_for_null_string_or_empty_string()
    {
        const string? argumentNull = null;
        var nullAction = () => Argument.IsNotNullOrEmpty(argumentNull);

        nullAction
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage("Required argument \"argumentNull\" was null. (Parameter 'argumentNull')");

        const string argumentEmpty = "";
        var emptyAction = () => Argument.IsNotNullOrEmpty(argumentEmpty);

        emptyAction
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("Required argument \"argumentEmpty\" was empty. (Parameter 'argumentEmpty')");
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
        IReadOnlyCollection<int>? argumentNull = null;
        Action nullAction = () => Argument.IsNotNullOrEmpty(argumentNull);

        nullAction
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage("Required argument \"argumentNull\" was null. (Parameter 'argumentNull')");

        IReadOnlyCollection<int> argumentEmpty = new List<int>();
        Action action = () => Argument.IsNotNullOrEmpty(argumentEmpty);

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("Required argument \"argumentEmpty\" was empty. (Parameter 'argumentEmpty')");
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
        IEnumerable<int>? argumentNull = null;
        Action nullAction = () => Argument.IsNotNullOrEmpty(argumentNull);

        nullAction
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage("Required argument \"argumentNull\" was null. (Parameter 'argumentNull')");

        IEnumerable<int> argumentEmpty = new List<int>();
        Action emptyAction = () => Argument.IsNotNullOrEmpty(argumentEmpty);

        emptyAction
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("Required argument \"argumentEmpty\" was empty. (Parameter 'argumentEmpty')");
    }

    [Fact]
    public void is_not_null_or_empty_returns_enumerable_for_valid_input()
    {
        // given
        IEnumerable<int> input = new List<int> { 1, 2, 3 };

        // when & then
        Argument.IsNotNullOrEmpty(input).Should().Equal(input);
    }
}
