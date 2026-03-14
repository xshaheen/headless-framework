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
