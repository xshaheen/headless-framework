// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class ArgumentIsNotNullTests
{
    [Fact]
    public void is_not_null_collection_with_items_does_not_null()
    {
        // given
        var collection = new List<int> { 1, 2, 3 };

        // when & then
        Argument.IsNotNull(collection).Should().NotBeNull();
    }

    [Fact]
    public void is_not_null_collection_throws_argument_exception()
    {
        // given
        List<int>? collection = null;

        // when & then
        Assert.Throws<ArgumentNullException>(() => Argument.IsNotNull(collection));
    }

    [Fact]
    public void is_not_null_string_with_value_does_not_throw()
    {
        // given
        var str = "test";

        // when & then
        Argument.IsNotNull(str).Should().NotBeNull(str);
    }
}
