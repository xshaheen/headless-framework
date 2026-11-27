// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using Azure.Storage.Blobs;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Models.Expiration;

namespace Tests.TestSetup;

/// <summary>
/// The real tusdotnet middleware over an Azure store that, as <c>UseAzure</c> wires it, reads the request's
/// <see cref="HttpContext" /> through an accessor, with every blob request counted, so a test sees the requests one
/// tus operation costs in production.
/// </summary>
internal sealed class TusAzureTestHost : IDisposable
{
    public const string Endpoint = "/files";

    private readonly IHost _host;

    private TusAzureTestHost(IHost host, TusAzureStore store, BlobRequestCounter counter, BlobServiceClient client)
    {
        _host = host;
        Store = store;
        Counter = counter;
        BlobServiceClient = client;
        Client = host.GetTestClient();
    }

    public TusAzureStore Store { get; }

    public BlobRequestCounter Counter { get; }

    /// <summary>A client outside the counter, for a test to read or change blobs behind the store's back.</summary>
    public BlobServiceClient BlobServiceClient { get; }

    public HttpClient Client { get; }

    public static async Task<TusAzureTestHost> StartAsync(
        TusAzureFixture fixture,
        TusAzureStoreOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        var counter = new BlobRequestCounter();
        var clientOptions = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04);
        clientOptions.AddPolicy(counter, Azure.Core.HttpPipelinePosition.PerCall);
        var countedClient = new BlobServiceClient(fixture.Container.GetConnectionString(), clientOptions);
        var plainClient = fixture.CreateBlobServiceClient();

        await new TusAzureContainerProvisioner(plainClient, options).ProvisionAsync(cancellationToken);

        // The configuration factory runs per request, after the host has started and the store exists.
        TusAzureStore? store = null;

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
            webHost
                .UseTestServer()
                .ConfigureServices(services => services.AddRouting().AddHttpContextAccessor())
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                        endpoints.MapTus(
                            Endpoint,
                            _ =>
                                Task.FromResult(
                                    new DefaultTusConfiguration
                                    {
                                        Store = store!,
                                        Expiration = new SlidingExpiration(TimeSpan.FromMinutes(30)),
                                    }
                                )
                        )
                    );
                })
        );

        var host = await builder.StartAsync(cancellationToken);

        store = new TusAzureStore(
            countedClient,
            options,
            loggerFactory: loggerFactory,
            httpContextAccessor: host.Services.GetRequiredService<IHttpContextAccessor>()
        );

        return new TusAzureTestHost(host, store, counter, plainClient);
    }

    public async Task<string> CreateUploadAsync(
        long uploadLength,
        string? metadata,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Length", uploadLength.ToString(CultureInfo.InvariantCulture));

        if (metadata is not null)
        {
            request.Headers.Add("Upload-Metadata", metadata);
        }

        using var response = await Client.SendAsync(request, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return response.Headers.Location!.ToString();
    }

    public async Task<HttpResponseMessage> HeadAsync(string location, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, location);
        request.Headers.Add("Tus-Resumable", "1.0.0");

        return await Client.SendAsync(request, cancellationToken);
    }

    public async Task<HttpResponseMessage> PatchAsync(
        string location,
        byte[] body,
        long offset,
        CancellationToken cancellationToken,
        byte[]? sha256 = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, location);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Offset", offset.ToString(CultureInfo.InvariantCulture));

        if (sha256 is not null)
        {
            request.Headers.Add("Upload-Checksum", $"sha256 {Convert.ToBase64String(sha256)}");
        }

        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");

        return await Client.SendAsync(request, cancellationToken);
    }

    public static string FileIdOf(string location)
    {
        return location[(location.LastIndexOf('/', StringComparison.Ordinal) + 1)..];
    }

    public void Dispose()
    {
        Client.Dispose();
        _host.Dispose();
    }
}
