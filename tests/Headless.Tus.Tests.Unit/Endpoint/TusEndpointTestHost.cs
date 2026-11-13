// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using Headless.Tus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tusdotnet.Stores;

namespace Tests.Endpoint;

/// <summary>What a scenario changes about <see cref="TusEndpointTestHost"/>. Every member has a working default.</summary>
internal sealed class TusEndpointTestHostSetup
{
    /// <summary>
    /// The <c>AddHeadlessTus</c> callback. It receives the disk store the host created and must select a store.
    /// The default selects that store and declares the default endpoint with a 1 MiB limit.
    /// </summary>
    public Action<HeadlessTusSetupBuilder, TusDiskStore> ConfigureTus { get; init; } =
        static (tus, store) =>
            tus.UseStore(store)
                .AddUploadEndpoint(static options => options.MaxSize = TusEndpointTestHost.DefaultMaxSize);

    /// <summary>Services the scenario adds beside <c>AddHeadlessTus</c>.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    /// <summary>Middleware that runs before the mapped endpoint.</summary>
    public Action<IApplicationBuilder>? ConfigureMiddleware { get; init; }

    /// <summary>The mapping call. The default maps the default endpoint at <see cref="TusEndpointTestHost.Route"/>.</summary>
    public Action<IEndpointRouteBuilder> Map { get; init; } =
        static endpoints => endpoints.MapHeadlessTus(TusEndpointTestHost.Route);

    /// <summary>
    /// Listens on a loopback port the operating system picks instead of the in-memory test server. Only a real
    /// server enforces a request body limit, so a scenario about that limit needs it.
    /// </summary>
    public bool UseKestrel { get; init; }
}

/// <summary>
/// A started host that serves upload endpoints declared through <c>AddHeadlessTus</c> over tusdotnet's disk store
/// in a temporary directory, with the client calls a tus scenario repeats.
/// </summary>
internal sealed class TusEndpointTestHost : IAsyncDisposable
{
    public const string Route = "/files";

    public const long DefaultMaxSize = 1024 * 1024;

    private readonly WebApplication _app;
    private readonly DirectoryInfo _directory;
    private readonly CancellationToken _cancellationToken;

    private TusEndpointTestHost(
        WebApplication app,
        DirectoryInfo directory,
        TusDiskStore store,
        HttpClient client,
        CancellationToken cancellationToken
    )
    {
        _app = app;
        _directory = directory;
        _cancellationToken = cancellationToken;
        Store = store;
        Client = client;
    }

    /// <summary>The disk store the host created over <see cref="Directory"/>.</summary>
    public TusDiskStore Store { get; }

    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>The directory the disk store keeps its files in; removed when the host is disposed.</summary>
    public string Directory => _directory.FullName;

    /// <summary>
    /// Builds and starts the host. A startup failure, such as invalid endpoint options, propagates. Every request the
    /// host sends afterwards is cancelled by <paramref name="cancellationToken"/>, the test's own token.
    /// </summary>
    public static async Task<TusEndpointTestHost> StartAsync(
        TusEndpointTestHostSetup? setup,
        CancellationToken cancellationToken
    )
    {
        setup ??= new TusEndpointTestHostSetup();

        var directory = System.IO.Directory.CreateTempSubdirectory("headless-tus-");
        WebApplication? app = null;

        try
        {
            var store = new TusDiskStore(directory.FullName);

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();

            if (setup.UseKestrel)
            {
                builder.WebHost.UseUrls("http://127.0.0.1:0");
            }
            else
            {
                builder.WebHost.UseTestServer();
            }

            builder.Services.AddHeadlessTus(tus => setup.ConfigureTus(tus, store));
            setup.ConfigureServices?.Invoke(builder.Services);

            app = builder.Build();
            setup.ConfigureMiddleware?.Invoke(app);
            setup.Map(app);

            await app.StartAsync(cancellationToken);

            var client = setup.UseKestrel
                ? new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }
                : app.GetTestClient();

            return new TusEndpointTestHost(app, directory, store, client, cancellationToken);
        }
        catch
        {
            if (app is not null)
            {
                await app.DisposeAsync();
            }

            directory.Delete(recursive: true);

            throw;
        }
    }

    /// <summary>Sends a request that carries <c>Tus-Resumable</c>, which every tus request except OPTIONS needs.</summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string uri,
        Action<HttpRequestMessage>? configure = null
    )
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        configure?.Invoke(request);

        return await Client.SendAsync(request, _cancellationToken);
    }

    /// <summary>
    /// Sends the creating POST. A <see langword="null"/> <paramref name="uploadLength"/> defers the length.
    /// </summary>
    public Task<HttpResponseMessage> CreateAsync(
        long? uploadLength,
        string? metadata = null,
        string route = Route,
        Action<HttpRequestMessage>? configure = null
    )
    {
        return SendAsync(
            HttpMethod.Post,
            route,
            request =>
            {
                if (uploadLength is null)
                {
                    request.Headers.Add("Upload-Defer-Length", "1");
                }
                else
                {
                    request.Headers.Add("Upload-Length", uploadLength.Value.ToString(CultureInfo.InvariantCulture));
                }

                if (metadata is not null)
                {
                    request.Headers.Add("Upload-Metadata", metadata);
                }

                configure?.Invoke(request);
            }
        );
    }

    /// <summary>Creates an upload that must succeed and returns its location.</summary>
    public async Task<string> CreateUploadAsync(long? uploadLength, string? metadata = null, string route = Route)
    {
        using var response = await CreateAsync(uploadLength, metadata, route);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return response.Headers.Location!.ToString();
    }

    public Task<HttpResponseMessage> PatchAsync(string location, byte[] body, long offset, long? uploadLength = null)
    {
        return SendAsync(
            HttpMethod.Patch,
            location,
            request =>
            {
                request.Headers.Add("Upload-Offset", offset.ToString(CultureInfo.InvariantCulture));

                if (uploadLength is not null)
                {
                    request.Headers.Add("Upload-Length", uploadLength.Value.ToString(CultureInfo.InvariantCulture));
                }

                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
            }
        );
    }

    public Task<HttpResponseMessage> HeadAsync(string location)
    {
        return SendAsync(HttpMethod.Head, location);
    }

    /// <summary>The file id is the last segment of the location a creating request answers with.</summary>
    public static string FileIdOf(string location)
    {
        return location[(location.LastIndexOf('/') + 1)..];
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        _directory.Delete(recursive: true);
    }
}
