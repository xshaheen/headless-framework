// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus;

namespace Tests.Azure;

public sealed class DefaultTusAzureBlobHttpHeadersProviderTests : TestBase
{
    private readonly DefaultTusAzureBlobHttpHeadersProvider _provider = new();

    [Theory]
    [InlineData("image/png", "image/png")]
    [InlineData("text/plain; charset=utf-8", "text/plain; charset=utf-8")]
    [InlineData("  video/mp4 ", "video/mp4")]
    public async Task should_use_the_filetype_metadata_when_it_is_a_media_type(string fileType, string expected)
    {
        // when
        var headers = await _provider.GetBlobHttpHeadersAsync(new(StringComparer.Ordinal) { ["filetype"] = fileType });

        // then
        headers.ContentType.Should().Be(expected);
        headers.CacheControl.Should().BeNull();
        headers.ContentDisposition.Should().BeNull();
        headers.ContentEncoding.Should().BeNull();
        headers.ContentLanguage.Should().BeNull();
        headers.ContentHash.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("image/")]
    [InlineData("image/png\r\nx-ms-blob-content-disposition: inline")]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/javascript; charset=utf-8")]
    public async Task should_fall_back_to_octet_stream_when_the_filetype_is_malformed_or_script_capable(string fileType)
    {
        // when
        var headers = await _provider.GetBlobHttpHeadersAsync(new(StringComparer.Ordinal) { ["filetype"] = fileType });

        // then
        headers.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task should_fall_back_to_octet_stream_without_a_filetype()
    {
        // when
        var headers = await _provider.GetBlobHttpHeadersAsync(new(StringComparer.Ordinal) { ["filename"] = "a.png" });

        // then
        headers.ContentType.Should().Be("application/octet-stream");
    }
}
