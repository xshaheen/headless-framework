// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IO;

namespace Tests.IO;

public sealed class FileNamesTests
{
    [Theory]
    [InlineData("_", "_")]
    [InlineData("hello-word", "hello-word")]
    // Remove invalid file name chars
    [InlineData("/hello/-/word/", "hello - word")]
    [InlineData("/hello/-/word/.png", "hello - word.png")]
    [InlineData("%hello\nword___)..", "%hello word___)..")]
    [InlineData("شاهين", "شاهين")]
    // Remove duplicated spaces
    [InlineData("hello   \n\t word", "hello word")]
    // HTML encode
    [InlineData("<>@hello \n   word&", "@hello word&amp;")]
    [InlineData("<>@hello \n   word&.png", "@hello word&amp;.png")]
    public void should_sanitize_as_expected_when_use_sanitize_file_name_method(string name, string expected)
    {
        // when
        var result = FileNames.SanitizeFileName(name);

        // then
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("<>@hello \n   word&.png", "_123", "@hello word&amp;.png", "hello_word_amp_123.png")]
    [InlineData("folder/report final (v1).pdf", "_42", "folder report final (v1).pdf", "folder_report_final_v1_42.pdf")]
    public void should_generate_trusted_file_names_with_suffix_as_expected(
        string untrustedName,
        string randomSuffix,
        string expectedDisplayName,
        string expectedUniqueSaveName
    )
    {
        // when
        var (trustedDisplayName, uniqueSaveName) = FileNames.GetTrustedFileName(untrustedName, randomSuffix);

        // then
        trustedDisplayName.Should().Be(expectedDisplayName);
        uniqueSaveName.Should().Be(expectedUniqueSaveName);
    }

    [Fact]
    public void should_generate_trusted_file_names_with_random_suffix()
    {
        // when
        var (trustedDisplayName, uniqueSaveName) = FileNames.GetTrustedFileName("<bad> name.png");

        // then
        trustedDisplayName.Should().Be("bad name.png");

        uniqueSaveName.Should().StartWith("bad_name_");
        uniqueSaveName.Should().EndWith(".png");

        var suffix = uniqueSaveName.Substring(
            "bad_name_".Length,
            uniqueSaveName.Length - "bad_name_".Length - ".png".Length
        );

        suffix.Should().NotBeNullOrEmpty();
        suffix.All(char.IsDigit).Should().BeTrue();
        suffix.Length.Should().BeGreaterThanOrEqualTo(7);
    }
}
