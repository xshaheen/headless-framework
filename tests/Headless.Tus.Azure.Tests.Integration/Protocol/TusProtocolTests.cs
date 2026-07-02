// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Headless.Testing.Tests;
using Headless.Tus.Options;
using Headless.Tus.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Models.Expiration;

namespace Tests.Protocol;

/// <summary>
/// Runs the REAL tusdotnet middleware pipeline (<c>MapTus</c> + TestServer) against the Azure
/// store, pinning the HTTP-visible behavior that store-level tests cannot: header echo on HEAD,
/// defer-length header switching, 409 on offset mismatch, 460 on checksum mismatch, and DELETE
/// termination. A store regression that only shows up through tusdotnet's requirements surfaces
/// here.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class TusProtocolTests(TusAzureFixture fixture) : TestBase
{
    private const string _Endpoint = "/files";

    [Fact]
    public async Task head_echoes_metadata_verbatim_and_reports_length_and_offset()
    {
        using var host = await _CreateHostAsync();
        using var client = host.GetTestClient();

        // given - creation with a metadata pair plus an empty-value key (legal per AllowEmptyValues)
        const string metadata = "filename dGVzdC5iaW4=,empty";
        var location = await _CreateUploadAsync(client, uploadLength: 100, metadata);

        // when
        using var head = await _HeadAsync(client, location);

        // then - byte-for-byte echo, exact length, zero offset
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Metadata").Should().ContainSingle().Which.Should().Be(metadata);
        head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be("100");
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
        head.Headers.GetValues("Tus-Resumable").Should().ContainSingle().Which.Should().Be("1.0.0");
    }

    [Fact]
    public async Task defer_length_upload_reports_defer_header_until_length_is_declared()
    {
        using var host = await _CreateHostAsync();
        using var client = host.GetTestClient();

        // given - creation without Upload-Length
        using var create = new HttpRequestMessage(HttpMethod.Post, _Endpoint);
        create.Headers.Add("Tus-Resumable", "1.0.0");
        create.Headers.Add("Upload-Defer-Length", "1");
        using var created = await client.SendAsync(create, AbortToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = created.Headers.Location!.ToString();

        // then - HEAD shows Upload-Defer-Length: 1 and no Upload-Length
        using (var head = await _HeadAsync(client, location))
        {
            head.Headers.GetValues("Upload-Defer-Length").Should().ContainSingle().Which.Should().Be("1");
            head.Headers.Contains("Upload-Length").Should().BeFalse();
        }

        // when - the first PATCH declares the final length
        var content = Faker.Random.Bytes(64);
        using var patched = await _PatchAsync(client, location, content, offset: 0, uploadLength: content.Length);
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // then - the length is now known and the defer header is gone
        using (var head = await _HeadAsync(client, location))
        {
            head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be("64");
            head.Headers.Contains("Upload-Defer-Length").Should().BeFalse();
        }
    }

    [Fact]
    public async Task patch_advances_offset_and_reports_expiration()
    {
        using var host = await _CreateHostAsync();
        using var client = host.GetTestClient();

        var content = Faker.Random.Bytes(256);
        var location = await _CreateUploadAsync(client, content.Length, metadata: null);

        // when
        using var patched = await _PatchAsync(client, location, content, offset: 0);

        // then - 204 with the new offset and (sliding expiration configured) Upload-Expires
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);
        patched.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("256");
        patched.Headers.Contains("Upload-Expires").Should().BeTrue();
    }

    [Fact]
    public async Task patch_with_wrong_offset_conflicts_without_modifying_the_upload()
    {
        using var host = await _CreateHostAsync();
        using var client = host.GetTestClient();

        var location = await _CreateUploadAsync(client, uploadLength: 100, metadata: null);

        // when - the client claims an offset the file is not at
        using var patched = await _PatchAsync(client, location, Faker.Random.Bytes(10), offset: 5);

        // then
        patched.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var head = await _HeadAsync(client, location);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
    }

    [Fact]
    public async Task patch_with_mismatching_checksum_responds_460_and_discards_the_chunk()
    {
        using var host = await _CreateHostAsync();
        using var client = host.GetTestClient();

        var content = Faker.Random.Bytes(128);
        var location = await _CreateUploadAsync(client, content.Length, metadata: null);

        // when - Upload-Checksum carries a digest of DIFFERENT bytes
        var wrongDigest = SHA256.HashData(Faker.Random.Bytes(128));
        using var patched = await _PatchAsync(client, location, content, offset: 0, checksum: wrongDigest);

        // then - 460 (checksum mismatch) and the chunk was not applied
        ((int)patched.StatusCode)
            .Should()
            .Be(460);

        using var head = await _HeadAsync(client, location);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");

        // and a clean retry at the same offset completes the upload
        var digest = SHA256.HashData(content);
        using var retried = await _PatchAsync(client, location, content, offset: 0, checksum: digest);
        retried.StatusCode.Should().Be(HttpStatusCode.NoContent);
        retried.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("128");
    }

    [Fact]
    public async Task delete_terminates_the_upload()
    {
        using var host = await _CreateHostAsync();
        using var client = host.GetTestClient();

        var location = await _CreateUploadAsync(client, uploadLength: 100, metadata: null);

        // when
        using var delete = new HttpRequestMessage(HttpMethod.Delete, location);
        delete.Headers.Add("Tus-Resumable", "1.0.0");
        using var deleted = await client.SendAsync(delete, AbortToken);

        // then
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var head = await _HeadAsync(client, location);
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<IHost> _CreateHostAsync()
    {
        var blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        var storeOptions = new TusAzureStoreOptions { ContainerName = "tusprotocol", BlobPrefix = "tusprotocol/" };
        var store = new TusAzureStore(blobServiceClient, storeOptions, loggerFactory: LoggerFactory);

        var configuration = new DefaultTusConfiguration
        {
            Store = store,
            Expiration = new SlidingExpiration(TimeSpan.FromMinutes(30)),
        };

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
            webHost
                .UseTestServer()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapTus(_Endpoint, _ => Task.FromResult(configuration)));
                })
        );

        return await builder.StartAsync(AbortToken);
    }

    private async Task<string> _CreateUploadAsync(HttpClient client, int uploadLength, string? metadata)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _Endpoint);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Length", uploadLength.ToString(CultureInfo.InvariantCulture));

        if (metadata is not null)
        {
            request.Headers.Add("Upload-Metadata", metadata);
        }

        using var response = await client.SendAsync(request, AbortToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return response.Headers.Location!.ToString();
    }

    private async Task<HttpResponseMessage> _HeadAsync(HttpClient client, string location)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, location);
        request.Headers.Add("Tus-Resumable", "1.0.0");

        return await client.SendAsync(request, AbortToken);
    }

    private async Task<HttpResponseMessage> _PatchAsync(
        HttpClient client,
        string location,
        byte[] body,
        long offset,
        int? uploadLength = null,
        byte[]? checksum = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, location);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Offset", offset.ToString(CultureInfo.InvariantCulture));

        if (uploadLength is not null)
        {
            request.Headers.Add("Upload-Length", uploadLength.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (checksum is not null)
        {
            request.Headers.Add("Upload-Checksum", $"sha256 {Convert.ToBase64String(checksum)}");
        }

        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");

        return await client.SendAsync(request, AbortToken);
    }
}
