// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Text;

namespace Tests.Text;

public sealed class IgnoreCaseStringComparerTests
{
    // Compare tests

    [Fact]
    public void compare_should_return_zero_when_both_null()
    {
        // given
        string? x = null;
        string? y = null;

        // when
        var result = IgnoreCaseStringComparer.Instance.Compare(x, y);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void compare_should_return_negative_when_x_is_null()
    {
        // given
        string? x = null;
        const string y = "test";

        // when
        var result = IgnoreCaseStringComparer.Instance.Compare(x, y);

        // then
        result.Should().Be(-1);
    }

    [Fact]
    public void compare_should_return_positive_when_y_is_null()
    {
        // given
        const string x = "test";
        string? y = null;

        // when
        var result = IgnoreCaseStringComparer.Instance.Compare(x, y);

        // then
        result.Should().Be(1);
    }

    [Theory]
    [InlineData("camelCase", "CamelCase")]
    [InlineData("PascalCase", "pascalCase")]
    [InlineData("snake_case", "snakeCase")]
    [InlineData("kebab-case", "kebabCase")]
    [InlineData("UPPER", "upper")]
    public void compare_should_return_zero_for_equivalent_identifiers(string x, string y)
    {
        // when
        var result = IgnoreCaseStringComparer.Instance.Compare(x, y);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void compare_should_return_negative_when_x_less_than_y()
    {
        // given
        const string x = "apple";
        const string y = "banana";

        // when
        var result = IgnoreCaseStringComparer.Instance.Compare(x, y);

        // then
        result.Should().BeNegative();
    }

    [Fact]
    public void compare_should_return_positive_when_x_greater_than_y()
    {
        // given
        const string x = "banana";
        const string y = "apple";

        // when
        var result = IgnoreCaseStringComparer.Instance.Compare(x, y);

        // then
        result.Should().BePositive();
    }

    // Equals tests

    [Fact]
    public void equals_should_return_false_when_x_is_null()
    {
        // given
        string? x = null;
        const string y = "test";

        // when
        var result = IgnoreCaseStringComparer.Instance.Equals(x, y);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void equals_should_return_false_when_y_is_null()
    {
        // given
        const string x = "test";
        string? y = null;

        // when
        var result = IgnoreCaseStringComparer.Instance.Equals(x, y);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void equals_should_return_false_when_both_null()
    {
        // given
        string? x = null;
        string? y = null;

        // when
        var result = IgnoreCaseStringComparer.Instance.Equals(x, y);

        // then
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("camelCase", "CamelCase")]
    [InlineData("PascalCase", "pascalCase")]
    [InlineData("snake_case", "snakeCase")]
    [InlineData("kebab-case", "kebabCase")]
    [InlineData("mixed_case-Style", "MixedCaseStyle")]
    [InlineData("UPPER_CASE", "upperCase")]
    [InlineData("lower_case", "LOWERCASE")]
    public void equals_should_return_true_for_equivalent_identifiers(string x, string y)
    {
        // when
        var result = IgnoreCaseStringComparer.Instance.Equals(x, y);

        // then
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("abc", "abcd")]
    [InlineData("abc", "ab")]
    [InlineData("test", "rest")]
    public void equals_should_return_false_for_different_strings(string x, string y)
    {
        // when
        var result = IgnoreCaseStringComparer.Instance.Equals(x, y);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void equals_should_ignore_non_alphanumeric_characters()
    {
        // given
        const string x = "get_user_by_id";
        const string y = "getUserById";

        // when
        var result = IgnoreCaseStringComparer.Instance.Equals(x, y);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void equals_should_handle_digits()
    {
        // given
        const string x = "item1";
        const string y = "Item_1";

        // when
        var result = IgnoreCaseStringComparer.Instance.Equals(x, y);

        // then
        result.Should().BeTrue();
    }

    // GetHashCode tests

    [Theory]
    [InlineData("camelCase", "CamelCase")]
    [InlineData("snake_case", "snakeCase")]
    [InlineData("kebab-case", "kebabCase")]
    public void get_hash_code_should_return_same_value_for_equivalent_identifiers(string x, string y)
    {
        // when
        var hashX = IgnoreCaseStringComparer.Instance.GetHashCode(x);
        var hashY = IgnoreCaseStringComparer.Instance.GetHashCode(y);

        // then
        hashX.Should().Be(hashY);
    }

    [Fact]
    public void get_hash_code_should_return_different_values_for_different_strings()
    {
        // given
        const string x = "apple";
        const string y = "banana";

        // when
        var hashX = IgnoreCaseStringComparer.Instance.GetHashCode(x);
        var hashY = IgnoreCaseStringComparer.Instance.GetHashCode(y);

        // then
        hashX.Should().NotBe(hashY);
    }

    [Fact]
    public void get_hash_code_should_handle_empty_string()
    {
        // given
        const string x = "";

        // when
        var hash = IgnoreCaseStringComparer.Instance.GetHashCode(x);

        // then
        hash.Should().Be(0);
    }

    [Fact]
    public void get_hash_code_should_handle_only_separators()
    {
        // given
        const string x = "___---";

        // when
        var hash = IgnoreCaseStringComparer.Instance.GetHashCode(x);

        // then
        hash.Should().Be(0);
    }

    // Instance tests

    [Fact]
    public void instance_should_be_singleton()
    {
        // when
        var instance1 = IgnoreCaseStringComparer.Instance;
        var instance2 = IgnoreCaseStringComparer.Instance;

        // then
        instance1.Should().BeSameAs(instance2);
    }

    // Dictionary usage test

    [Fact]
    public void should_work_as_dictionary_comparer()
    {
        // given
        var dictionary = new Dictionary<string, int>(IgnoreCaseStringComparer.Instance) { ["camelCase"] = 1 };

        // when/then
        dictionary.Should().ContainKey("CamelCase");
        dictionary.Should().ContainKey("camel_case");
        dictionary.Should().ContainKey("camel-case");
        dictionary["CAMELCASE"].Should().Be(1);
    }
}
