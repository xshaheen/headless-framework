// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.Aws;
using Headless.Testing.Tests;

namespace Tests;

public sealed class AwsBlobNamingNormalizerTests : TestBase
{
    private readonly AwsBlobNamingNormalizer _sut = new();

    #region NormalizeContainerName Tests

    [Fact]
    public void should_lowercase_container_name()
    {
        var result = _sut.NormalizeContainerName("MyBucket");

        result.Should().Be("mybucket");
    }

    [Fact]
    public void should_truncate_to_63_characters()
    {
        var longName = new string('a', 70);

        var result = _sut.NormalizeContainerName(longName);

        result.Should().HaveLength(63);
    }

    [Theory]
    [InlineData("my_bucket!", "mybucket")]
    [InlineData("my@bucket#test", "mybuckettest")]
    [InlineData("BUCKET_NAME_123", "bucketname123")]
    public void should_remove_invalid_characters(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("my..bucket", "my.bucket")]
    [InlineData("my...bucket", "my.bucket")]
    [InlineData("a....b", "a.b")]
    public void should_remove_multiple_consecutive_periods(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("my-.bucket", "mybucket")]
    [InlineData("test-.name", "testname")]
    public void should_remove_hyphen_before_period(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("my.-bucket", "mybucket")]
    [InlineData("test.-name", "testname")]
    public void should_remove_period_before_hyphen(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void should_remove_leading_hyphen()
    {
        // Regex only removes single leading hyphen per application
        var result = _sut.NormalizeContainerName("-mybucket");

        result.Should().Be("mybucket");
    }

    [Fact]
    public void should_remove_trailing_hyphen()
    {
        // Regex only removes single trailing hyphen per application
        var result = _sut.NormalizeContainerName("mybucket-");

        result.Should().Be("mybucket");
    }

    [Theory]
    [InlineData(".mybucket", "mybucket")]
    [InlineData("..mybucket", "mybucket")]
    public void should_remove_leading_period(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("mybucket.", "mybucket")]
    [InlineData("mybucket..", "mybucket")]
    public void should_remove_trailing_period(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void should_preserve_ip_like_strings()
    {
        // Note: The IP address regex is intentionally permissive;
        // normal IP addresses pass through as-is since they are valid bucket names
        var result = _sut.NormalizeContainerName("192.168.1.1");

        result.Should().Be("192.168.1.1");
    }

    [Theory]
    [InlineData("ab", "ab0")]
    [InlineData("a", "a00")]
    [InlineData("x", "x00")]
    public void should_pad_short_names_to_minimum_3(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("abcd", "abcd")]
    [InlineData("my-bucket", "my-bucket")]
    public void should_not_pad_names_with_3_or_more_chars(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("!!!", "000")]
    [InlineData("@@@", "000")]
    [InlineData("___", "000")]
    public void should_handle_empty_after_normalization(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("my-bucket.name", "my-bucket.name")]
    [InlineData("valid-bucket-123", "valid-bucket-123")]
    [InlineData("test.bucket.name", "test.bucket.name")]
    public void should_preserve_valid_characters(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    #endregion

    #region NormalizeBlobName Tests

    [Theory]
    [InlineData("file.txt")]
    [InlineData("folder/file.txt")]
    [InlineData("folder/subfolder/file.txt")]
    public void should_return_valid_blob_name(string blobName)
    {
        var result = _sut.NormalizeBlobName(blobName);

        result.Should().Be(blobName);
    }

    [Theory]
    [InlineData("../file.txt")]
    [InlineData("folder/../file.txt")]
    [InlineData("..\\file.txt")]
    [InlineData("folder\\..\\file.txt")]
    public void should_throw_for_path_traversal(string blobName)
    {
        var act = () => _sut.NormalizeBlobName(blobName);

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(blobName)).WithMessage("*path traversal*");
    }

    [Theory]
    [InlineData("/file.txt")]
    [InlineData("\\file.txt")]
    [InlineData("/folder/file.txt")]
    public void should_throw_for_absolute_path(string blobName)
    {
        var act = () => _sut.NormalizeBlobName(blobName);

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(blobName)).WithMessage("*Absolute paths*");
    }

    [Theory]
    [InlineData("file\0.txt")]
    [InlineData("file\n.txt")]
    [InlineData("file\t.txt")]
    public void should_throw_for_control_characters(string blobName)
    {
        var act = () => _sut.NormalizeBlobName(blobName);

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(blobName)).WithMessage("*Control characters*");
    }

    [Fact]
    public void should_allow_blob_name_with_double_dots_not_followed_by_slash()
    {
        var result = _sut.NormalizeBlobName("file..name.txt");

        result.Should().Be("file..name.txt");
    }

    #endregion
}
