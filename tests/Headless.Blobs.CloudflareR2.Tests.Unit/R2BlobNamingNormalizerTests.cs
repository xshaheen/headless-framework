// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.CloudflareR2;
using Headless.Testing.Tests;

namespace Tests;

public sealed class R2BlobNamingNormalizerTests : TestBase
{
    private readonly R2BlobNamingNormalizer _sut = new();

    [Theory]
    [InlineData("my.bucket", "mybucket")]
    [InlineData("my.bucket.name", "mybucketname")]
    [InlineData("a.b.c", "abc")]
    public void should_remove_dots(string input, string expected)
    {
        _sut.NormalizeContainerName(input).Should().Be(expected);
    }

    [Fact]
    public void should_lowercase_container_name()
    {
        _sut.NormalizeContainerName("MyBucket").Should().Be("mybucket");
    }

    [Fact]
    public void should_truncate_to_63_characters()
    {
        _sut.NormalizeContainerName(new string('a', 70)).Should().HaveLength(63);
    }

    [Theory]
    [InlineData("my_bucket!", "mybucket")]
    [InlineData("my@bucket#test", "mybuckettest")]
    public void should_remove_invalid_characters(string input, string expected)
    {
        _sut.NormalizeContainerName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("-mybucket", "mybucket")]
    [InlineData("--mybucket", "mybucket")]
    [InlineData("mybucket-", "mybucket")]
    [InlineData("mybucket--", "mybucket")]
    public void should_trim_leading_and_trailing_hyphens(string input, string expected)
    {
        _sut.NormalizeContainerName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("ab", "ab0")]
    [InlineData("a", "a00")]
    public void should_pad_short_names_to_minimum_3(string input, string expected)
    {
        _sut.NormalizeContainerName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("...", "000")]
    [InlineData("@@@", "000")]
    public void should_handle_empty_after_normalization(string input, string expected)
    {
        _sut.NormalizeContainerName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("valid-bucket-123")]
    [InlineData("my-bucket")]
    public void should_preserve_valid_names(string input)
    {
        _sut.NormalizeContainerName(input).Should().Be(input);
    }

    [Fact]
    public void should_return_valid_blob_name()
    {
        _sut.NormalizeBlobName("folder/file.txt").Should().Be("folder/file.txt");
    }

    [Fact]
    public void should_throw_for_blob_path_traversal()
    {
        var act = () => _sut.NormalizeBlobName("../file.txt");

        act.Should().Throw<ArgumentException>();
    }
}
