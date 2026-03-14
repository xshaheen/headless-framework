using Headless.Text;

namespace Tests.Text;

public sealed class FormatStringTokenizerTests
{
    [Theory]
    [InlineData("open bracket { only ")]
    [InlineData("nested {0{1}} tokens")]
    [InlineData("}")]
    [InlineData("} wrong format")]
    [InlineData("{0 wrong format")]
    [InlineData(" wrong 0} format")]
    [InlineData(" wrong {} format")]
    public void should_throw_format_exception_when_invalid_format_string(string format)
    {
        FluentActions.Invoking(() => FormatStringTokenizer.Tokenize(format)).Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("", new string[] { })]
    [InlineData("a sample {0} value", new[] { "a sample ", "{0}", " value" })]
    [InlineData("{0} is {name} at this {1}.", new[] { "{0}", " is ", "{name}", " at this ", "{1}", "." })]
    public void TokenizeTest(string format, params string[] expectedTokens)
    {
        var actualTokens = FormatStringTokenizer.Tokenize(format);

        if (expectedTokens.IsNullOrEmpty())
        {
            actualTokens.Should().BeEmpty();

            return;
        }

        actualTokens.Should().HaveCount(expectedTokens.Length);

        for (var i = 0; i < actualTokens.Count; i++)
        {
            var actualToken = actualTokens[i];
            var expectedToken = expectedTokens[i];

            actualToken.Text.Should().Be(expectedToken.Trim('{', '}'));

            if (expectedToken.StartsWith('{') && expectedToken.EndsWith('}'))
            {
                actualToken.Type.Should().Be(FormatStringTokenType.DynamicValue);
            }
            else
            {
                actualToken.Type.Should().Be(FormatStringTokenType.ConstantText);
            }
        }
    }
}
