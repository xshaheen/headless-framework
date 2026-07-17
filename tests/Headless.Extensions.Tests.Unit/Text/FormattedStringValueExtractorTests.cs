using Headless.Primitives;
using Headless.Text;

namespace Tests.Text;

public sealed class FormattedStringValueExtractorTests
{
    [Theory]
    [InlineData("User X does not exist.", "User {0} does not exist.", "X")]
    [InlineData("User Mahmoud Shaheen does not exist.", "User {0} does not exist.", "Mahmoud Shaheen")]
    public void is_match_tests(string input, string format, params string[] expectedValues)
    {
        var isMatch = FormattedStringValueExtractor.IsMatch(input, format, out var values);

        isMatch.Should().Be(true);
        values.Should().BeEquivalentTo(expectedValues);
    }

    [Theory]
    [InlineData("My name is Shaheen.", "My name is Ahmed.")]
    [InlineData("Role {0} does not exist.", "User name {0} is invalid, can only contain letters or digits.")]
    [InlineData("{0} cannot be null or empty.", "Incorrect password.")]
    [InlineData("Incorrect password.", "{0} cannot be null or empty.")]
    public void extract_not_match_false(string input, string format)
    {
        var result = FormattedStringValueExtractor.Extract(input, format);

        result.IsMatch.Should().Be(false);
    }

    [Theory]
    [InlineData("My name is Neo.", "My name is {0}.", new[] { "0", "Neo" })]
    [InlineData(
        "User Mahmoud Shaheen does not exist in Company.",
        "User {0} does not exist in {1}.",
        new[] { "0", "Mahmoud Shaheen", "1", "Company" }
    )]
    [InlineData(
        "User Mahmoud Shaheen does not exist in Shaheen Limited database.",
        "User {Name} does not exist in {CompanyName} database.",
        new[] { "Name", "Mahmoud Shaheen", "CompanyName", "Shaheen Limited" }
    )]
    public void extract_test_is_match_true(string input, string format, string[]? expectedPairs)
    {
        // given
        var nameValuePairs = _ConvertToNameValuePairs(expectedPairs);

        // when
        var result = FormattedStringValueExtractor.Extract(input, format);

        // then
        result.IsMatch.Should().Be(true);

        if (nameValuePairs.Length == 0)
        {
            result.Matches.Should().BeEmpty();

            return;
        }

        result.Matches.Should().HaveCount(nameValuePairs.Length);

        for (var i = 0; i < nameValuePairs.Length; i++)
        {
            var actualMatch = result.Matches[i];
            var expectedPair = nameValuePairs[i];
            actualMatch.Should().BeEquivalentTo(expectedPair);
        }
    }

    [Fact]
    public void should_not_match_when_extract_input_has_trailing_text_after_final_constant()
    {
        // given
        // The format ends with the constant ".", but "abc.def" has trailing "def" that the format
        // can not consume, so the whole input is not matched.
        const string input = "abc.def";
        const string format = "{x}.";

        // when
        var result = FormattedStringValueExtractor.Extract(input, format);

        // then
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void should_match_when_extract_input_ends_with_final_constant()
    {
        // given
        const string input = "abc.";
        const string format = "{x}.";

        // when
        var result = FormattedStringValueExtractor.Extract(input, format);

        // then
        result.IsMatch.Should().BeTrue();
        result.Matches.Should().ContainSingle();
        result.Matches[0].Should().BeEquivalentTo(new NameValue { Name = "x", Value = "abc" });
    }

    [Fact]
    public void should_be_false_when_is_match_trailing_text_after_final_constant()
    {
        // when
        var isMatch = FormattedStringValueExtractor.IsMatch("abc.def", "{x}.", out var values);

        // then
        isMatch.Should().BeFalse();
        values.Should().BeEmpty();
    }

    [Fact]
    public void extract_trailing_dynamic_value_greedily_keeps_separator_literal()
    {
        // given
        // The dynamic value {b} itself contains the separator "-"; the trailing dynamic value
        // greedily captures the remaining input including the extra separator (documented limitation).
        const string input = "x-y-z";
        const string format = "{a}-{b}";

        // when
        var result = FormattedStringValueExtractor.Extract(input, format);

        // then
        result.IsMatch.Should().BeTrue();
        result.Matches.Should().HaveCount(2);
        result.Matches[0].Should().BeEquivalentTo(new NameValue { Name = "a", Value = "x" });
        result.Matches[1].Should().BeEquivalentTo(new NameValue { Name = "b", Value = "y-z" });
    }

    [Fact]
    public void extract_splits_on_first_separator_occurrence_when_all_placeholders_present()
    {
        // given
        const string input = "x-y-z";
        const string format = "{a}-{b}-{c}";

        // when
        var result = FormattedStringValueExtractor.Extract(input, format);

        // then
        result.IsMatch.Should().BeTrue();
        result.Matches.Should().HaveCount(3);
        result.Matches[0].Should().BeEquivalentTo(new NameValue { Name = "a", Value = "x" });
        result.Matches[1].Should().BeEquivalentTo(new NameValue { Name = "b", Value = "y" });
        result.Matches[2].Should().BeEquivalentTo(new NameValue { Name = "c", Value = "z" });
    }

    private static NameValue[] _ConvertToNameValuePairs(string[]? expectedPairs)
    {
        if (expectedPairs is null)
        {
            return [];
        }

        var nameValuePairs = new NameValue[expectedPairs.Length / 2];

        for (var i = 0; i < expectedPairs.Length; i += 2)
        {
            nameValuePairs[i / 2] = new NameValue { Name = expectedPairs[i], Value = expectedPairs[i + 1] };
        }

        return nameValuePairs;
    }
}
