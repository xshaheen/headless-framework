// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Validators;

namespace Tests.Validators;

public sealed class EnumNameValidatorTests
{
    private enum Color
    {
        Red,
        Green,
        Blue,
    }

    [Theory]
    [InlineData("Red")]
    [InlineData("Green")]
    [InlineData("Blue")]
    public void should_return_true_for_defined_member_name(string name)
    {
        EnumNameValidator.IsDefinedName<Color>(name).Should().BeTrue();
        EnumNameValidator.IsDefinedName<Color>(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("red")]
    [InlineData("BLUE")]
    [InlineData("Yellow")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(" ")]
    public void should_return_false_for_non_member_name_when_case_sensitive(string name)
    {
        EnumNameValidator.IsDefinedName<Color>(name).Should().BeFalse();
        EnumNameValidator.IsDefinedName<Color>(name).Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_null()
    {
        EnumNameValidator.IsDefinedName(typeof(Color), name: null).Should().BeFalse();
        EnumNameValidator.IsDefinedName<Color>(name: null).Should().BeFalse();
    }

    [Theory]
    [InlineData("red")]
    [InlineData("GREEN")]
    [InlineData("Blue")]
    public void should_match_case_insensitively_when_requested(string name)
    {
        EnumNameValidator.IsDefinedName<Color>(name, ignoreCase: true).Should().BeTrue();
        EnumNameValidator.IsDefinedName<Color>(name, ignoreCase: true).Should().BeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("Yellow")]
    public void should_reject_numeric_and_unknown_even_when_case_insensitive(string name)
    {
        EnumNameValidator.IsDefinedName<Color>(name, ignoreCase: true).Should().BeFalse();
    }

    [Fact]
    public void get_names_returns_all_members()
    {
        EnumNameValidator.GetNames(typeof(Color)).Should().BeEquivalentTo("Red", "Green", "Blue");
    }

    [Fact]
    public void get_names_caches_per_type_and_case()
    {
        var first = EnumNameValidator.GetNames(typeof(Color));
        var second = EnumNameValidator.GetNames(typeof(Color));

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void should_throw_for_non_enum_type()
    {
        Action act = () => EnumNameValidator.IsDefinedName(typeof(int), "Red");

        act.Should().Throw<ArgumentException>();
    }
}
