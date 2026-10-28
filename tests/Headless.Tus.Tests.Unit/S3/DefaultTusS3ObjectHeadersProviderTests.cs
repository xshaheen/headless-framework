// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus;

namespace Tests.S3;

public sealed class DefaultTusS3ObjectHeadersProviderTests : TestBase
{
    private readonly DefaultTusS3ObjectHeadersProvider _provider = new();

    [Theory]
    [InlineData("image/png", "image/png")]
    [InlineData("text/plain; charset=utf-8", "text/plain; charset=utf-8")]
    [InlineData("  video/mp4 ", "video/mp4")]
    public async Task should_use_the_filetype_metadata_when_it_is_a_media_type(string fileType, string expected)
    {
        // when
        var headers = await _provider.GetObjectHeadersAsync(new(StringComparer.Ordinal) { ["filetype"] = fileType });

        // then
        headers.ContentType.Should().Be(expected);
        headers.CacheControl.Should().BeNull();
        headers.ContentDisposition.Should().BeNull();
        headers.ContentEncoding.Should().BeNull();
        headers.ContentLanguage.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("png")]
    [InlineData("image/")]
    [InlineData("image/png\r\nx-amz-acl: public-read")]
    public async Task should_fall_back_to_octet_stream_when_the_filetype_is_not_a_media_type(string fileType)
    {
        // when
        var headers = await _provider.GetObjectHeadersAsync(new(StringComparer.Ordinal) { ["filetype"] = fileType });

        // then
        headers.ContentType.Should().Be("application/octet-stream");
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/javascript; charset=utf-8")]
    public async Task should_fall_back_to_octet_stream_when_the_filetype_is_script_capable(string fileType)
    {
        // when
        var headers = await _provider.GetObjectHeadersAsync(new(StringComparer.Ordinal) { ["filetype"] = fileType });

        // then
        headers.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task should_fall_back_to_octet_stream_without_a_filetype()
    {
        // when - only differently cased or unrelated keys
        var headers = await _provider.GetObjectHeadersAsync(
            new(StringComparer.Ordinal) { ["FileType"] = "image/png", ["filename"] = "a.png" }
        );

        // then - tus clients send the key in lower case, and keys are matched as sent
        headers.ContentType.Should().Be("application/octet-stream");
    }
}
