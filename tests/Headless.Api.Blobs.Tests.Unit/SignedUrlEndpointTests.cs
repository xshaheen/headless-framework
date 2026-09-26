// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using Headless.Blobs;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SignedUrlEndpointTests : TestBase
{
    private static readonly BlobLocation _Report = new(SignedUrlTestApp.Container, "2026/q3.pdf");

    [Fact]
    public async Task should_stream_blob_with_derived_content_type_and_safety_headers_when_download_url_is_valid()
    {
        // given
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken);
        await _UploadAsync(app.DefaultStorage, _Report, "report-bytes");
        var url = await _Presigned(app.DefaultStorage)
            .GetPresignedDownloadUrlAsync(_Report, TimeSpan.FromMinutes(15), AbortToken);
        using var client = app.CreateClient();

        // when
        using var response = await client.GetAsync(url, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("Content-Security-Policy").Should().Equal("sandbox");
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be("report-bytes");
    }

    [Fact]
    public async Task should_return_not_found_when_download_url_has_expired()
    {
        // given
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken);
        await _UploadAsync(app.DefaultStorage, _Report, "report-bytes");
        var url = await _Presigned(app.DefaultStorage)
            .GetPresignedDownloadUrlAsync(_Report, TimeSpan.FromMinutes(15), AbortToken);
        using var client = app.CreateClient();

        // when
        app.Time.Advance(TimeSpan.FromMinutes(15));
        using var response = await client.GetAsync(url, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_return_not_found_when_token_is_tampered()
    {
        // given
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken);
        await _UploadAsync(app.DefaultStorage, _Report, "report-bytes");
        var url = await _Presigned(app.DefaultStorage)
            .GetPresignedDownloadUrlAsync(_Report, TimeSpan.FromMinutes(15), AbortToken);
        // Flip a character that carries only payload bits, so the token stays valid base64url but fails authentication.
        var tampered = url.Segments[^1].ToCharArray();
        tampered[^2] = tampered[^2] == 'A' ? 'B' : 'A';
        using var client = app.CreateClient();

        // when
        using var response = await client.GetAsync(new Uri(url, new string(tampered)), AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_return_not_found_when_blob_does_not_exist()
    {
        // given
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken);
        var url = await _Presigned(app.DefaultStorage)
            .GetPresignedDownloadUrlAsync(_Report, TimeSpan.FromMinutes(15), AbortToken);
        using var client = app.CreateClient();

        // when
        using var response = await client.GetAsync(url, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_return_not_found_when_download_url_is_used_to_upload()
    {
        // given
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken);
        var url = await _Presigned(app.DefaultStorage)
            .GetPresignedDownloadUrlAsync(_Report, TimeSpan.FromMinutes(15), AbortToken);
        using var client = app.CreateClient();

        // when
        using var body = new StringContent("overwrite");
        using var response = await client.PutAsync(url, body, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await app.DefaultStorage.ExistsAsync(_Report, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_bind_url_to_its_store_when_same_location_exists_in_another_store()
    {
        // given
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken);
        await _UploadAsync(app.DefaultStorage, _Report, "default-bytes");
        await _UploadAsync(app.NamedStorage, _Report, "named-bytes");
        var url = await _Presigned(app.NamedStorage)
            .GetPresignedDownloadUrlAsync(_Report, TimeSpan.FromMinutes(15), AbortToken);
        using var client = app.CreateClient();

        // when
        using var response = await client.GetAsync(url, AbortToken);

        // then
        (await response.Content.ReadAsStringAsync(AbortToken))
            .Should()
            .Be("named-bytes");
    }

    [Fact]
    public async Task should_store_body_when_upload_satisfies_constraints()
    {
        // given
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken);
        var url = await _Presigned(app.DefaultStorage)
            .GetPresignedUploadUrlAsync(
                _Report,
                TimeSpan.FromMinutes(15),
                new PresignedUploadConstraints { ContentType = "application/pdf", MaxLength = 64 },
                AbortToken
            );
        using var client = app.CreateClient();
        using var body = _Body("uploaded-bytes", "application/pdf");

        // when
        using var response = await client.PutAsync(url, body, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var stored = await app.DefaultStorage.OpenReadStreamAsync(_Report, AbortToken);
        using var reader = new StreamReader(stored!.Stream);
        (await reader.ReadToEndAsync(AbortToken)).Should().Be("uploaded-bytes");
    }

    [Fact]
    public async Task should_reject_upload_with_unsupported_media_type_when_content_type_differs()
    {
        // given
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken);
        var url = await _Presigned(app.DefaultStorage)
            .GetPresignedUploadUrlAsync(
                _Report,
                TimeSpan.FromMinutes(15),
                new PresignedUploadConstraints { ContentType = "application/pdf" },
                AbortToken
            );
        using var client = app.CreateClient();
        using var body = _Body("<script>alert(1)</script>", "text/html");

        // when
        using var response = await client.PutAsync(url, body, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await app.DefaultStorage.ExistsAsync(_Report, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_reject_upload_as_too_large_when_body_exceeds_max_length()
    {
        // given
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken);
        var url = await _Presigned(app.DefaultStorage)
            .GetPresignedUploadUrlAsync(
                _Report,
                TimeSpan.FromMinutes(15),
                new PresignedUploadConstraints { MaxLength = 4 },
                AbortToken
            );
        using var client = app.CreateClient();
        using var body = _Body("more-than-four-bytes", "application/pdf");

        // when
        using var response = await client.PutAsync(url, body, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await app.DefaultStorage.ExistsAsync(_Report, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_require_content_length_when_upload_has_max_length()
    {
        // given
        await using var app = await SignedUrlTestApp.StartAsync(AbortToken);
        var url = await _Presigned(app.DefaultStorage)
            .GetPresignedUploadUrlAsync(
                _Report,
                TimeSpan.FromMinutes(15),
                new PresignedUploadConstraints { MaxLength = 64 },
                AbortToken
            );
        using var client = app.CreateClient();
        using var body = new StreamContent(new UnknownLengthStream("abc"u8.ToArray()));

        // when
        using var response = await client.PutAsync(url, body, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.LengthRequired);
    }

    private static IPresignedUrlBlobStorage _Presigned(IBlobStorage storage)
    {
        return storage.Should().BeAssignableTo<IPresignedUrlBlobStorage>().Subject;
    }

    private static async Task _UploadAsync(IBlobStorage storage, BlobLocation location, string content)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await storage.UploadAsync(location, stream, cancellationToken: AbortToken);
    }

    private static ByteArrayContent _Body(string content, string contentType)
    {
        var body = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        body.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        return body;
    }

    /// <summary>A readable stream that cannot report its length, so the client sends the body without Content-Length.</summary>
    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
