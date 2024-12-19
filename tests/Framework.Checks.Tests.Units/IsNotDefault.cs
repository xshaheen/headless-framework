// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class IsNotDefault
{
    [Fact]
    public void is_default_should_not_throw_if_argument_is_default()
    {
        // given
        int defaultValue = 0;

        // when & then
        Argument.IsDefault(defaultValue);
    }

    [Fact]
    public void is_default_should_throw_if_argument_is_not_default()
    {
        // given
        int nonDefaultValue = 05;

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.IsDefault(nonDefaultValue))
            .Message.Should().Contain("must be default.");
    }

    [Fact]
    public void is_not_default_should_return_argument_if_not_default()
    {
        // given
        int nonDefaultValue = 10;

        // when & then
        Argument.IsNotDefault(nonDefaultValue).Should().Be(nonDefaultValue);
    }

    [Fact]
    public void is_not_default_should_throw_if_argument_is_default()
    {
        // given
        int defaultValue = default;

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.IsNotDefault(defaultValue))
            .Message.Should().Contain("cannot be the default value");
    }

    [Fact]
    public void is_not_default_should_return_nullable_argument_if_not_default()
    {
        // given
        int? nonDefaultValue = 15;

        // when & then
        Argument.IsNotDefault(nonDefaultValue).Should().Be(nonDefaultValue);
    }

    [Fact]
    public void is_not_default_or_null_should_return_argument_if_not_default_or_null()
    {
        // given
        int? nonDefaultValue = 20;

        // when & then
        Argument.IsNotDefaultOrNull(nonDefaultValue).Should().Be(nonDefaultValue);

    }

    [Fact]
    public void is_not_default_or_null_should_throw_if_argument_is_null()
    {
        // given
        int? nullValue = null;

        // when & then
        Assert.Throws<ArgumentNullException>(() => Argument.IsNotDefaultOrNull(nullValue))
            .Message.Should().Contain($"\"{nameof(nullValue)}\" was null.");
    }
}
