// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs.SshNet;

namespace Tests;

public sealed class SshBlobNamingNormalizerTests
{
    private readonly SshBlobNamingNormalizer _normalizer = new();

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
    public void NormalizeContainerName_should_remove_invalid_characters(string input, string expected)
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
    public void NormalizeBlobName_should_remove_invalid_characters(string input, string expected)
    {
        var result = _normalizer.NormalizeBlobName(input);
        result.Should().Be(expected);
    }
}
