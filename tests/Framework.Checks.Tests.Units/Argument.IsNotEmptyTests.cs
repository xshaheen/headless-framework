// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public sealed class ArgumentIsNotEmptyTests
{
    [Fact]
    public void is_not_empty_collection_with_items_does_not_null()
    {
        // given
        var collection = new List<int> { 1, 2, 3 };

        // when & then
        Argument.IsNotEmpty(collection).Should().NotBeEmpty();
    }

    [Fact]
    public void is_not_empty_empty_collection_throws_argument_exception()
    {
        // given
        var collection = new List<int>();

        // when
        var action = () => Argument.IsNotEmpty(collection);

        // then
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void is_not_empty_string_with_value_does_not_throw()
    {
        // given
        const string str = "test";

        // when & then
        Argument.IsNotEmpty(str).Should().NotBeNull(str);
    }
}
