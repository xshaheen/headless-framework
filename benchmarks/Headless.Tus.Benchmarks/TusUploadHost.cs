// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Amazon.S3;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using tusdotnet;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Expiration;
using tusdotnet.Stores.FileIdProviders;
using TusdotnetS3Store = tusdotnet.Stores.S3.TusS3Store;
using TusdotnetS3StoreConfiguration = tusdotnet.Stores.S3.TusS3StoreConfiguration;

namespace Headless.Tus.Benchmarks;

/// <summary>
/// The real tusdotnet middleware on Kestrel over one S3 store, driven by a tus client over loopback HTTP, so a run
/// pays every store call a tus request makes in production, in tusdotnet's own order. Both stores sit behind the same
/// configuration: a 30-minute sliding expiration, the setup the community store's README shows.
/// </summary>
internal sealed class TusUploadHost : IAsyncDisposable
{
    private const string _Endpoint = "/files";

    private readonly WebApplication _app;
    private readonly AmazonS3Client _s3;
    private readonly HttpClient _client;

    private TusUploadHost(WebApplication app, AmazonS3Client s3, StorageRequestCounter counter, Uri baseAddress)
    {
        _app = app;
        _s3 = s3;
        Counter = counter;
        _client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromMinutes(5) };
    }

    /// <summary>Every storage request the store under test sends.</summary>
    public StorageRequestCounter Counter { get; }

    public static async Task<TusUploadHost> StartAsync(TusStoreKind kind, MinioServer minio)
    {
        var bucket = kind == TusStoreKind.Headless ? "headless-tus" : "tusdotnet-s3";
        await minio.CreateBucketAsync(bucket);

#pragma warning disable CA2000 // Owned by the host: disposed with the S3 client in DisposeAsync.
        var counter = new StorageRequestCounter();
        var s3 = minio.CreateClient(new CountingHttpClientFactory(counter));
#pragma warning restore CA2000

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders().AddSimpleConsole().SetMinimumLevel(LogLevel.Error);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpContextAccessor();

        var app = builder.Build();
        var store = _CreateStore(kind, s3, bucket, app.Services.GetRequiredService<IHttpContextAccessor>());

        app.MapTus(
            _Endpoint,
            _ =>
                Task.FromResult(
                    new DefaultTusConfiguration
                    {
                        Store = store,
                        Expiration = new SlidingExpiration(TimeSpan.FromMinutes(30)),
                    }
                )
        );

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;

        return new TusUploadHost(app, s3, counter, new Uri(address.First()));
    }

    /// <summary>Sends one whole upload the way <paramref name="profile" /> describes and returns its location.</summary>
    public async Task<string> UploadAsync(UploadProfile profile, byte[] payload)
    {
        var deferred = profile == UploadProfile.DeferredLength;
        var location = await _CreateAsync(deferred ? null : payload.Length);
        var chunk = profile.ChunkSize();

        for (var offset = 0; offset < payload.Length; offset += chunk)
        {
            var count = Math.Min(chunk, payload.Length - offset);
            var declareLength = deferred && offset + count == payload.Length;

            using var request = new HttpRequestMessage(HttpMethod.Patch, location);
            request.Headers.Add("Tus-Resumable", "1.0.0");
            request.Headers.Add("Upload-Offset", offset.ToString(CultureInfo.InvariantCulture));

            if (declareLength)
            {
                request.Headers.Add("Upload-Length", payload.Length.ToString(CultureInfo.InvariantCulture));
            }

            request.Content = new ByteArrayContent(payload, offset, count);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");

            using var response = await _client.SendAsync(request);
            await _EnsureAsync(
                response,
                HttpStatusCode.NoContent,
                $"PATCH at offset {offset.ToString(CultureInfo.InvariantCulture)}"
            );
        }

        return location;
    }

    /// <summary>Terminates an upload so the next iteration starts from an equally full bucket.</summary>
    public async Task DeleteAsync(string location)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, location);
        request.Headers.Add("Tus-Resumable", "1.0.0");

        using var response = await _client.SendAsync(request);
        await _EnsureAsync(response, HttpStatusCode.NoContent, "DELETE");
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        _s3.Dispose();
        Counter.Dispose();
    }

    private async Task<string> _CreateAsync(long? uploadLength)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _Endpoint);
        request.Headers.Add("Tus-Resumable", "1.0.0");

        if (uploadLength is { } length)
        {
            request.Headers.Add("Upload-Length", length.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            request.Headers.Add("Upload-Defer-Length", "1");
        }

        using var response = await _client.SendAsync(request);
        await _EnsureAsync(response, HttpStatusCode.Created, "POST");

        return response.Headers.Location!.ToString();
    }

    private static ITusStore _CreateStore(
        TusStoreKind kind,
        IAmazonS3 s3,
        string bucket,
        IHttpContextAccessor httpContextAccessor
    )
    {
        return kind switch
        {
            TusStoreKind.Headless => new TusS3Store(
                s3,
                new TusS3StoreOptions
                {
                    Bucket = bucket,
                    Provider = TusS3Provider.S3Compatible,
                    PartSize = UploadProfiles.PartSize,
                },
                httpContextAccessor: httpContextAccessor
            ),
            TusStoreKind.TusdotnetS3 => new TusdotnetS3Store(
                NullLogger<TusdotnetS3Store>.Instance,
                new TusdotnetS3StoreConfiguration
                {
                    BucketName = bucket,
                    PreferredPartSizeInBytes = UploadProfiles.PartSize,
                },
                s3,
                new GuidFileIdProvider()
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
        };
    }

    private static async Task _EnsureAsync(HttpResponseMessage response, HttpStatusCode expected, string request)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();

            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"tus {request} answered {(int)response.StatusCode} instead of {(int)expected}: {body}"
                )
            );
        }
    }
}
