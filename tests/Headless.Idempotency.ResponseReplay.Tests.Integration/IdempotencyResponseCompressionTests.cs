// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using Headless.Api;
using Headless.Http;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA2025 // False positive: _PostAsync awaits SendAsync before its request disposes; tests only hold the outer task.

namespace Tests;

/// <summary>
/// Response compression outside the idempotency middleware, the order <c>UseHeadless()</c> composes: the capture sees
/// the handler's plain bytes while the compressor labels the shared response headers, so a replay must leave the
/// encoding to the compressor that runs on the retry.
/// </summary>
public sealed class IdempotencyResponseCompressionTests : TestBase
{
    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task should_replay_a_decodable_body_when_response_compression_wraps_idempotency(string encoding)
    {
        var key = $"k-{Guid.NewGuid():N}";
        await using var app = await _CreateAppAsync();
        using var client = _CreateDecompressingClient(app);

        using var first = await _PostAsync(client, key, encoding);
        using var second = await _PostAsync(client, key, encoding);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.GetValues(HttpHeaderNames.IdempotentReplayed).Should().ContainSingle().Which.Should().Be("true");

        // Reading the content decodes it as its Content-Encoding says; plain bytes labelled compressed fail here.
        var firstBody = await first.Content.ReadAsStringAsync(AbortToken);
        var secondBody = await second.Content.ReadAsStringAsync(AbortToken);
        firstBody.Should().Contain("\"invocation\"");
        secondBody.Should().Be(firstBody, "the retry replays the stored response");
    }

    [Fact]
    public async Task should_replay_uncompressed_when_the_retry_does_not_accept_an_encoding()
    {
        var key = $"k-{Guid.NewGuid():N}";
        await using var app = await _CreateAppAsync();
        using var client = _CreateDecompressingClient(app);

        using var first = await _PostAsync(client, key, "br");
        using var second = await _PostAsync(client, key, acceptEncoding: null);

        second.Headers.GetValues(HttpHeaderNames.IdempotentReplayed).Should().ContainSingle().Which.Should().Be("true");
        second.Content.Headers.ContentEncoding.Should().BeEmpty();
        (await second.Content.ReadAsStringAsync(AbortToken))
            .Should()
            .Be(await first.Content.ReadAsStringAsync(AbortToken));
    }

    private static Task<WebApplication> _CreateAppAsync()
    {
        return IdempotencyTestApp.CreateAsync(
            static services =>
            {
                services.AddHeadlessIdempotency(static setup =>
                {
                    setup.UseInMemory();
                    setup.ConfigureOptions(static options => options.PurgeInterval = null);
                });

                services.AddHeadlessApiResponseCompression();

                // The test host serves plain HTTP; enabling HTTPS too keeps the setup valid for either scheme.
                services.Configure<Microsoft.AspNetCore.ResponseCompression.ResponseCompressionOptions>(static o =>
                    o.EnableForHttps = true
                );
            },
            configureOuterPipeline: static app => app.UseResponseCompression()
        );
    }

    private static HttpClient _CreateDecompressingClient(WebApplication app)
    {
#pragma warning disable CA2000 // HttpClient disposes the handler it owns.
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CheckCertificateRevocationList = true,
        };
#pragma warning restore CA2000
        return new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri(app.Urls.Single()) };
    }

    private static async Task<HttpResponseMessage> _PostAsync(HttpClient client, string key, string? acceptEncoding)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo");
        request.Content = new StringContent("hello");
        request.Headers.Add(HttpHeaderNames.IdempotencyKey, key);

        // Set per request (automatic decompression would otherwise advertise every encoding), so the test controls
        // which encoding the compressor picks.
        request.Headers.AcceptEncoding.Clear();
        if (acceptEncoding is not null)
        {
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue(acceptEncoding));
        }

        return await client.SendAsync(request, cancellationToken: AbortToken);
    }
}
