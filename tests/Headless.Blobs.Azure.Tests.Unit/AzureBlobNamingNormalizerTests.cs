// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.Azure;

namespace Tests;

public sealed class AzureBlobNamingNormalizerTests
{
    private readonly AzureBlobNamingNormalizer _sut = new();

    [Fact]
    public void should_throw_argument_exception_when_blob_name_contains_forward_slash_path_traversal()
    {
        var act = () => _sut.NormalizeBlobName("../malicious.txt");

        act.Should().Throw<ArgumentException>().WithParameterName("blobName").WithMessage("*path traversal*");
    }

    [Fact]
    public void should_throw_argument_exception_when_blob_name_contains_backslash_path_traversal()
    {
        var act = () => _sut.NormalizeBlobName("..\\malicious.txt");

        act.Should().Throw<ArgumentException>().WithParameterName("blobName").WithMessage("*path traversal*");
    }

    [Fact]
    public void should_throw_argument_exception_when_blob_name_contains_embedded_path_traversal()
    {
        var act = () => _sut.NormalizeBlobName("folder/../other/malicious.txt");

        act.Should().Throw<ArgumentException>().WithParameterName("blobName").WithMessage("*path traversal*");
    }

    [Fact]
    public void should_allow_valid_blob_name_without_path_traversal()
    {
        var result = _sut.NormalizeBlobName("folder/subfolder/file.txt");

        result.Should().Be("folder/subfolder/file.txt");
    }

    [Fact]
    public void should_allow_blob_name_with_double_dots_not_followed_by_slash()
    {
        var result = _sut.NormalizeBlobName("file..name.txt");

        result.Should().Be("file..name.txt");
    }

    #region NormalizeContainerName

    [Theory]
    [InlineData("MyContainer", "mycontainer")]
    [InlineData("UPPERCASE", "uppercase")]
    [InlineData("MixedCase", "mixedcase")]
    public void should_lowercase_container_name(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void should_truncate_to_63_characters()
    {
        var input = new string('a', 70);

        var result = _sut.NormalizeContainerName(input);

        result.Should().HaveLength(63);
        result.Should().Be(new string('a', 63));
    }

    [Theory]
    [InlineData("my_container!", "mycontainer")]
    [InlineData("my@container#test", "mycontainertest")]
    [InlineData("container.name", "containername")]
    public void should_remove_invalid_characters(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("my--container", "my-container")]
    [InlineData("my---container", "my-container")]
    [InlineData("a--b--c", "a-b-c")]
    public void should_remove_consecutive_dashes(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("-mycontainer", "mycontainer")]
    [InlineData("--mycontainer", "mycontainer")]
    public void should_remove_leading_dash(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("mycontainer-", "mycontainer")]
    [InlineData("mycontainer--", "mycontainer")]
    public void should_remove_trailing_dash(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void should_pad_short_names_to_minimum_3()
    {
        var result = _sut.NormalizeContainerName("ab");

        result.Should().Be("ab0");
    }

    [Fact]
    public void should_pad_very_short_names()
    {
        var result = _sut.NormalizeContainerName("a");

        result.Should().Be("a00");
    }

    [Fact]
    public void should_not_pad_names_with_3_or_more_chars()
    {
        var result = _sut.NormalizeContainerName("abc");

        result.Should().Be("abc");
    }

    [Fact]
    public void should_handle_empty_after_normalization()
    {
        var result = _sut.NormalizeContainerName("---");

        result.Should().Be("000");
    }

    [Theory]
    [InlineData("my-container1", "my-container1")]
    [InlineData("abc-def-123", "abc-def-123")]
    public void should_preserve_valid_characters(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void should_use_invariant_culture()
    {
        // Turkish I issue: 'I'.ToLower() in Turkish culture is 'ı' (dotless i)
        // With invariant culture it should be 'i'
        var result = _sut.NormalizeContainerName("ITEM");

        result.Should().Be("item");
    }

    #endregion
}
