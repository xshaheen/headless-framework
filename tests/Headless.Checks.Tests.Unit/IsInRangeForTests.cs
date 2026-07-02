// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class IsInRangeForTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void is_in_range_for_should_return_index_when_in_range(int index)
    {
        Argument.IsInRangeFor(index, 3).Should().Be(index);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(100)]
    public void is_in_range_for_should_throw_when_out_of_range(int index)
    {
        var action = () => Argument.IsInRangeFor(index, 3);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_in_range_for_should_format_message()
    {
        const int index = 5;
        var action = () => Argument.IsInRangeFor(index, 3);

        action
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage(
                "The argument \"index\" = 5 must be a valid index for a collection of 3 item(s) (Valid range [0, 2]). (Parameter 'index')"
            );
    }

    [Fact]
    public void is_in_range_for_collection_should_validate_against_count()
    {
        var collection = new[] { 10, 20, 30 };

        Argument.IsInRangeFor(2, collection).Should().Be(2);

        var action = () => Argument.IsInRangeFor(3, collection);
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_in_range_for_collection_should_throw_argument_null_when_null()
    {
        var action = () => Argument.IsInRangeFor(0, (IReadOnlyCollection<int>)null!);
        action.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void is_in_range_for_span_should_validate_against_length()
    {
        Span<int> span = [10, 20, 30];

        Argument.IsInRangeFor(1, span).Should().Be(1);

        var threw = false;
        try
        {
            Argument.IsInRangeFor(3, span);
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        threw.Should().BeTrue();
    }
}
