// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class IsNullTests
{
    [Fact]
    public void is_null_collection_with_items_does_not_null()
    {
        // given
        ICollection<int>? collection = null;

        // when & then
        Argument.IsNull(collection).Should().BeNull();
    }

    [Fact]
    public void is_collection_not_null_throws_argument_null_exception()
    {
        // given
        var collection = new List<int> { 1, 2, 3, 4 };
        const string customMessage = $"Error {nameof(collection)} is not null";

        // when
        Action action = () => Argument.IsNull(collection);
        Action actionWithCustomMessage = () => Argument.IsNull(collection, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage("The argument \"collection\" must be null. (Parameter 'collection')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage($"{customMessage} (Parameter 'collection')");
    }

    [Fact]
    public void is_null_string_with_null_does_not_throw()
    {
        // given
        const string? str = null;

        // when & then
        Argument.IsNull(str).Should().BeNull(str);
    }

    [Fact]
    public void is_null_nullable_struct_with_null_returns_null()
    {
        // given
        int? value = null;

        // when & then
        Argument.IsNull(value).Should().BeNull();
    }

    [Fact]
    public void is_null_nullable_struct_with_value_throws()
    {
        // given
        int? value = 5;
        const string customMessage = "value must be null";

        // when
        Action action = () => Argument.IsNull(value);
        Action actionWithCustomMessage = () => Argument.IsNull(value, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage("The argument \"value\" must be null. (Parameter 'value')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage($"{customMessage} (Parameter 'value')");
    }
}
