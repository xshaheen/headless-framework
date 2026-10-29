// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus;

namespace Tests.S3;

public sealed class DefaultTusS3ObjectHeadersProviderTests : TestBase
{
    private readonly DefaultTusS3ObjectHeadersProvider _provider = new();

    [Theory]
    [InlineData("image/png")]
    [InlineData("text/html")]
    [InlineData("image/png\r\nx-amz-acl: public-read")]
    public async Task should_store_octet_stream_whatever_the_filetype(string fileType)
    {
        // when
        var headers = await _provider.GetObjectHeadersAsync(new(StringComparer.Ordinal) { ["filetype"] = fileType });

        // then
        headers.ContentType.Should().Be("application/octet-stream");
        headers.CacheControl.Should().BeNull();
        headers.ContentDisposition.Should().BeNull();
        headers.ContentEncoding.Should().BeNull();
        headers.ContentLanguage.Should().BeNull();
    }
}
