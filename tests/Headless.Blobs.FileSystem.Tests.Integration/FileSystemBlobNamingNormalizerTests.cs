// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;

namespace Tests;

public sealed class FileSystemBlobNamingNormalizerTests
{
    private readonly CrossOsNamingNormalizer _normalizer = new();

    [Theory]
    [InlineData("validfilename", "validfilename")]
    [InlineData("invalid/filename", "invalidfilename")]
    [InlineData("invalid\\filename", "invalidfilename")]
    [InlineData("invalid:filename", "invalidfilename")]
    [InlineData("invalid*filename", "invalidfilename")]
    [InlineData("invalid?filename", "invalidfilename")]
    [InlineData("invalid\"filename", "invalidfilename")]
    [InlineData("invalid<filename", "invalidfilename")]
    [InlineData("invalid>filename", "invalidfilename")]
    [InlineData("invalid|filename", "invalidfilename")]
    public void should_remove_invalid_characters_when_normalize_container_name(string input, string expected)
    {
        var result = _normalizer.NormalizeContainerName(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("validfilename", "validfilename")]
    [InlineData("invalid/filename", "invalidfilename")]
    [InlineData("invalid\\filename", "invalidfilename")]
    [InlineData("invalid:filename", "invalidfilename")]
    [InlineData("invalid*filename", "invalidfilename")]
    [InlineData("invalid?filename", "invalidfilename")]
    [InlineData("invalid\"filename", "invalidfilename")]
    [InlineData("invalid<filename", "invalidfilename")]
    [InlineData("invalid>filename", "invalidfilename")]
    [InlineData("invalid|filename", "invalidfilename")]
    public void should_remove_invalid_characters_when_normalize_blob_name(string input, string expected)
    {
        var result = _normalizer.NormalizeBlobName(input);
        result.Should().Be(expected);
    }
}
