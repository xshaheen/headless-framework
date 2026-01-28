// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class IsNotEmptyTests
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
        var customMessage = $"Error {nameof(collection)} not contains elements.";

        // when
        var action = () => Argument.IsNotEmpty(collection);
        var actionWithCustomMessage = () => Argument.IsNotEmpty(collection, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("Required argument \"collection\" was empty. (Parameter 'collection')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'collection')");
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
