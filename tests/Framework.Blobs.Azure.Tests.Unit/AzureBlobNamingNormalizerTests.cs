// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs.Azure;

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
}
