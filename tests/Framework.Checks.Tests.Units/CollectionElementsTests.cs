// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public sealed class CollectionElementsTests
{
    [Fact]
    public void has_no_nulls_throws_exception_if_argument_contains_null()
    {
        // given
        IReadOnlyCollection<string?> argument = new List<string?> { "value1", null, "value3" };

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.HasNoNulls(argument))
            .Message.Should().Contain("cannot contains null elements.");
    }

    [Fact]
    public void has_no_nulls_returns_argument_if_no_null_elements()
    {
        // given
        IReadOnlyCollection<string?> argument = new List<string?> { "value1", "value2", "value3" };

        // when
        var result = Argument.HasNoNulls(argument);

        // then
        result.Should().Equal(argument);
    }

    [Fact]
    public void has_no_null_or_empty_elements_throws_exception_if_argument_contains_empty_element()
    {
        // given
        IReadOnlyCollection<string?> argument = new List<string?> { "value1", "", "value3" };

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.HasNoNullOrEmptyElements(argument))
            .Message.Should().Contain("cannot contains empty elements.");
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

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.HasNoNullOrWhiteSpaceElements(argument))
            .Message.Should().Contain("cannot contains empty or white space elements.");
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

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.HasNoNullOrWhiteSpaceElements(argument))
            .Message.Should().Contain("cannot contains empty or white space elements.");
    }
}
