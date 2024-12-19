// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class IsNotNullOrEmptyTests
{
    [Fact]
    public void is_not_null_or_empty_throws_for_null_string_or_empty_string()
    {
        // given
        string? argumentNull = null;
        string argumentEmpty = "";

        // when & then
        Assert.Throws<ArgumentNullException>(
                () =>
                    Argument.IsNotNullOrEmpty(argumentNull)
            )
            .Message.Should().Contain($"\"{nameof(argumentNull)}\" was null.");

        Assert.Throws<ArgumentException>(
                () =>
                    Argument.IsNotNullOrEmpty(argumentEmpty)
            )
            .Message.Should().Contain($"\"{nameof(argumentEmpty)}\" was empty.");
    }

    [Fact]
    public void is_not_null_or_empty_returns_string_for_valid_input()
    {
        // given
        string input = "valid input";

        // when & then
        Argument.IsNotNullOrEmpty(input).Should().Be(input);
    }

    [Fact]
    public void is_not_null_or_empty_throws_for_null_or_empty_readonly_collection()
    {
        // given
        IReadOnlyCollection<int>? inputIsNull = null;
        IReadOnlyCollection<int> inputIsEmpty = new List<int>();

        // when & then
        Assert.Throws<ArgumentNullException>(
                () =>
                    Argument.IsNotNullOrEmpty(inputIsNull)
            )
            .Message.Should().Contain($"\"{nameof(inputIsNull)}\" was null.");

        Assert.Throws<ArgumentException>(
                () =>
                    Argument.IsNotNullOrEmpty(inputIsEmpty)
            )
            .Message.Should().Contain($"\"{nameof(inputIsEmpty)}\" was empty.");
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
        IEnumerable<int>? inputIsNull = null;
        IEnumerable<int> inputIsEmpty = new List<int>();

        // when & then
        Assert.Throws<ArgumentNullException>(
                () =>
                    Argument.IsNotNullOrEmpty(inputIsNull)
            )
            .Message.Should().Contain($"\"{nameof(inputIsNull)}\" was null.");

        Assert.Throws<ArgumentException>(
                () =>
                    Argument.IsNotNullOrEmpty(inputIsEmpty)
            )
            .Message.Should().Contain($"\"{nameof(inputIsEmpty)}\" was empty.");
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
