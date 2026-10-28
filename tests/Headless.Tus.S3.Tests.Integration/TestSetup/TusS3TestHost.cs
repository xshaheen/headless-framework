// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus;
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
/// The real tusdotnet middleware over an S3 store that, as <c>AddTusS3Store</c> wires it, reads the request's
/// <see cref="HttpContext" /> through an accessor, so a test sees the requests one tus operation costs in production.
/// </summary>
internal sealed class TusS3TestHost : IDisposable
{
    public const string Endpoint = "/files";

    private readonly IHost _host;

    private TusS3TestHost(IHost host, TusS3Store store)
    {
        _host = host;
        Store = store;
        Client = host.GetTestClient();
    }

    public TusS3Store Store { get; }

    public HttpClient Client { get; }

    public static async Task<TusS3TestHost> StartAsync(
        S3TestClient s3,
        TusS3StoreOptions options,
        ILoggerFactory loggerFactory,
        TimeProvider? timeProvider,
        CancellationToken cancellationToken
    )
    {
        // The configuration factory runs per request, after the host has started and the store exists.
        TusS3Store? store = null;

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

        store = new TusS3Store(
            s3.S3,
            options,
            loggerFactory: loggerFactory,
            timeProvider: timeProvider,
            httpContextAccessor: host.Services.GetRequiredService<IHttpContextAccessor>()
        );

        return new TusS3TestHost(host, store);
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
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        return response.Headers.Location!.ToString();
    }

    public async Task<HttpResponseMessage> HeadAsync(string location, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, location);
        request.Headers.Add("Tus-Resumable", "1.0.0");

        return await Client.SendAsync(request, cancellationToken);
    }

    public static string FileIdOf(string location)
    {
        return location[(location.LastIndexOf('/') + 1)..];
    }

    public void Dispose()
    {
        Client.Dispose();
        _host.Dispose();
    }
}
