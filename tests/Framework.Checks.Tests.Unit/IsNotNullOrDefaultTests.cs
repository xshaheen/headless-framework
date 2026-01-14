// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class IsNotNullOrDefaultTests
{
    [Fact]
    public void is_not_null_or_default_with_value_returns_unwrapped_value()
    {
        // given
        int? value = 42;

        // when & then
        Argument.IsNotNullOrDefault(value).Should().Be(42);
    }

    [Fact]
    public void is_not_null_or_default_with_null_throws()
    {
        // given
        int? argument = null;
        const string customMessage = "Error argument is null";

        // when
        Action action = () => Argument.IsNotNullOrDefault(argument);
        Action actionWithCustomMessage = () => Argument.IsNotNullOrDefault(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage($"Required argument \"{nameof(argument)}\" was null. (Parameter '{nameof(argument)}')");

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentNullException>()
            .WithMessage($"{customMessage} (Parameter '{nameof(argument)}')");
    }

    [Fact]
    public void is_not_null_or_default_with_default_value_throws()
    {
        // given
        int? argument = 0;
        const string customMessage = "Error argument is default";

        // when
        Action action = () => Argument.IsNotNullOrDefault(argument);
        Action actionWithCustomMessage = () => Argument.IsNotNullOrDefault(argument, customMessage);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage(
                "The argument \"argument\" can NOT be the default value of <System.Int32>. (Parameter 'argument')"
            );

        actionWithCustomMessage
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"{customMessage} (Parameter 'argument')");
    }

    [Fact]
    public void is_not_null_or_default_with_guid_empty_throws()
    {
        // given
        Guid? argument = Guid.Empty;

        // when
        Action action = () => Argument.IsNotNullOrDefault(argument);

        // then
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void is_not_null_or_default_with_valid_guid_returns_value()
    {
        // given
        Guid? value = Guid.NewGuid();

        // when & then
        Argument.IsNotNullOrDefault(value).Should().Be(value.Value);
    }

    [Fact]
    public void is_not_null_or_default_with_datetime_default_throws()
    {
        // given
        DateTime? argument = default(DateTime);

        // when
        Action action = () => Argument.IsNotNullOrDefault(argument);

        // then
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void is_not_null_or_default_with_valid_datetime_returns_value()
    {
        // given
        DateTime? value = DateTime.Now;

        // when & then
        Argument.IsNotNullOrDefault(value).Should().Be(value.Value);
    }
}
