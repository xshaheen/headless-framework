// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class IsNotNullTests
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
        const string customMessage = $"Error {nameof(collection)} is null";

        // when
        Action action = () => Argument.IsNotNull(collection);
        Action actionWithCustomMessage = () => Argument.IsNotNull(collection, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage($"Required argument \"{nameof(collection)}\" was null. (Parameter '{nameof(collection)}')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage($"{customMessage} (Parameter '{nameof(collection)}')");
    }

    [Fact]
    public void is_not_null_string_with_value_does_not_throw()
    {
        // given
        const string str = "test";

        // when & then
        Argument.IsNotNull(str).Should().NotBeNull(str);
    }

    [Fact]
    public void should_return_same_instance_when_not_null()
    {
        // given
        var obj = new object();

        // when
        var result = Argument.IsNotNull(obj);

        // then
        result.Should().BeSameAs(obj);
    }

    [Fact]
    public void should_include_param_name_in_exception()
    {
        // given
        object? myParameter = null;

        // when
        var action = () => Argument.IsNotNull(myParameter);

        // then
        action.Should().ThrowExactly<ArgumentNullException>().WithParameterName("myParameter");
    }
}
