// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using tusdotnet;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Expiration;

namespace Tests;

/// <summary>
/// Runs the REAL tusdotnet middleware pipeline (<c>MapTus</c> + TestServer) against the store under test, pinning the
/// HTTP-visible behavior that store-level tests cannot: header echo on HEAD, defer-length header switching, 409 on
/// offset mismatch, and DELETE termination. A store regression that only shows up through tusdotnet's requirements
/// surfaces here.
/// </summary>
public abstract class TusProtocolConformanceTestsBase<TStore>(ITusConformanceBackend<TStore> backend)
    : TusConformanceTestsBase<TStore>(backend)
    where TStore : class,
        ITusStore,
        ITusPipelineStore,
        ITusCreationStore,
        ITusCreationDeferLengthStore,
        ITusTerminationStore,
        ITusExpirationStore,
        ITusReadableStore,
        ITusConcatenationStore
{
    /// <summary>The route the tus endpoint is mapped to.</summary>
    protected const string Endpoint = "/files";

    public virtual async Task head_echoes_metadata_verbatim_and_reports_length_and_offset()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        // given - creation with a metadata pair plus an empty-value key (legal per AllowEmptyValues)
        const string metadata = "filename dGVzdC5iaW4=,empty";
        var location = await CreateUploadAsync(client, uploadLength: 100, metadata);

        // when
        using var head = await HeadAsync(client, location);

        // then - byte-for-byte echo, exact length, zero offset
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Metadata").Should().ContainSingle().Which.Should().Be(metadata);
        head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be("100");
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
        head.Headers.GetValues("Tus-Resumable").Should().ContainSingle().Which.Should().Be("1.0.0");
    }

    public virtual async Task defer_length_upload_reports_defer_header_until_length_is_declared()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        // given - creation without Upload-Length
        using var create = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        create.Headers.Add("Tus-Resumable", "1.0.0");
        create.Headers.Add("Upload-Defer-Length", "1");
        using var created = await client.SendAsync(create, AbortToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = created.Headers.Location!.ToString();

        // then - HEAD shows Upload-Defer-Length: 1 and no Upload-Length
        using (var head = await HeadAsync(client, location))
        {
            head.Headers.GetValues("Upload-Defer-Length").Should().ContainSingle().Which.Should().Be("1");
            head.Headers.Contains("Upload-Length").Should().BeFalse();
        }

        // when - the first PATCH declares the final length
        var content = Faker.Random.Bytes(64);
        using var patched = await PatchAsync(client, location, content, offset: 0, uploadLength: content.Length);
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // then - the length is now known and the defer header is gone
        using (var head = await HeadAsync(client, location))
        {
            head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be("64");
            head.Headers.Contains("Upload-Defer-Length").Should().BeFalse();
        }
    }

    public virtual async Task patch_advances_offset_and_reports_expiration()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var content = Faker.Random.Bytes(256);
        var location = await CreateUploadAsync(client, content.Length, metadata: null);

        // when
        using var patched = await PatchAsync(client, location, content, offset: 0);

        // then - 204 with the new offset and (sliding expiration configured) Upload-Expires
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);
        patched.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("256");
        patched.Headers.Contains("Upload-Expires").Should().BeTrue();
    }

    public virtual async Task patch_with_wrong_offset_conflicts_without_modifying_the_upload()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var location = await CreateUploadAsync(client, uploadLength: 100, metadata: null);

        // when - the client claims an offset the file is not at
        using var patched = await PatchAsync(client, location, Faker.Random.Bytes(10), offset: 5);

        // then
        patched.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var head = await HeadAsync(client, location);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
    }

    public virtual async Task delete_terminates_the_upload()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var location = await CreateUploadAsync(client, uploadLength: 100, metadata: null);

        // when
        using var delete = new HttpRequestMessage(HttpMethod.Delete, location);
        delete.Headers.Add("Tus-Resumable", "1.0.0");
        using var deleted = await client.SendAsync(delete, AbortToken);

        // then
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var head = await HeadAsync(client, location);
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Starts a test host that maps the tus endpoint over the store under test.</summary>
    protected Task<IHost> CreateHostAsync()
    {
        return StartHostAsync(Store);
    }

    /// <summary>Starts a test host that maps the tus endpoint over <paramref name="store" />.</summary>
    protected static async Task<IHost> StartHostAsync(ITusStore store)
    {
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
                    app.UseEndpoints(endpoints => endpoints.MapTus(Endpoint, _ => Task.FromResult(configuration)));
                })
        );

        return await builder.StartAsync(AbortToken);
    }

    protected static async Task<string> CreateUploadAsync(HttpClient client, int uploadLength, string? metadata)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
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

    protected static async Task<HttpResponseMessage> HeadAsync(HttpClient client, string location)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, location);
        request.Headers.Add("Tus-Resumable", "1.0.0");

        return await client.SendAsync(request, AbortToken);
    }

    protected static async Task<HttpResponseMessage> PatchAsync(
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
