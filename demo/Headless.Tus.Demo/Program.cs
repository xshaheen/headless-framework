// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Mime;
using System.Text;
using Azure.Storage.Blobs;
using Demo.Services;
using Headless.Tus;
using Headless.Tus.Services;
using Microsoft.Extensions.Azure;
using Microsoft.Net.Http.Headers;
using tusdotnet;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Expiration;

const string corsPolicy = "tus-demo";

// tus response headers the browser client must be able to read cross-origin (Vite dev server).
string[] tusExposedHeaders =
[
    "Location",
    "Tus-Resumable",
    "Tus-Version",
    "Tus-Extension",
    "Tus-Max-Size",
    "Tus-Checksum-Algorithm",
    "Upload-Offset",
    "Upload-Length",
    "Upload-Defer-Length",
    "Upload-Metadata",
    "Upload-Expires",
    "Upload-Concat",
];

var builder = WebApplication.CreateBuilder(args);

// Azurite by default ("UseDevelopmentStorage=true"); point ConnectionStrings:AzureStorage at a
// real storage account to run against Azure. The pinned service version keeps the SDK compatible
// with the Azurite emulator, which lags behind the newest Azure Storage API versions.
builder.Services.AddAzureClients(clients =>
    clients
        .AddBlobServiceClient(builder.Configuration.GetConnectionString("AzureStorage") ?? "UseDevelopmentStorage=true")
        .WithVersion(BlobClientOptions.ServiceVersion.V2024_11_04)
);

// Registers TusAzureStore as a singleton with validated options bound from the "Tus" section.
builder.Services.AddTusAzureStore(builder.Configuration.GetSection("Tus"));

builder.Services.AddSingleton<UploadDirectory>();
builder.Services.AddHostedService<ExpiredUploadsCleanupService>();

builder.Services.AddCors(options =>
    options.AddPolicy(
        corsPolicy,
        policy =>
            policy
                .WithOrigins("http://localhost:5173") // Vite dev server
                .AllowAnyHeader()
                .WithMethods("GET", "POST", "PATCH", "HEAD", "DELETE", "OPTIONS")
                .WithExposedHeaders(tusExposedHeaders)
    )
);

var app = builder.Build();

app.UseCors(corsPolicy);

app.MapTus(
    "/files",
    httpContext =>
    {
        var tusConfiguration = new DefaultTusConfiguration
        {
            // Resolved from DI — registered by AddTusAzureStore above.
            Store = httpContext.RequestServices.GetRequiredService<TusAzureStore>(),
            // For multi-node deployments, register Headless.Tus.DistributedLocks and set
            // FileLockProvider from DI. Omitting it uses tusdotnet's in-process lock, which is
            // sufficient for this single-node demo.
            UsePipelinesIfAvailable = true,
            AllowedExtensions = TusExtensions.All,
            MaxAllowedUploadSizeInBytesLong = 2L * 1024 * 1024 * 1024, // 2 GB demo cap
            // Unfinished uploads expire after 30 idle minutes and are removed by the cleanup
            // service; completed uploads are never removed by expiration.
            Expiration = new SlidingExpiration(TimeSpan.FromMinutes(30)),
        };

        return Task.FromResult(tusConfiguration);
    }
);

var api = app.MapGroup("/api");

api.MapGet(
    "/files",
    async (UploadDirectory directory, CancellationToken cancellationToken) =>
        Results.Ok(await directory.ListAsync(cancellationToken))
);

api.MapGet(
    "/files/{fileId}/download",
    async (string fileId, TusAzureStore store, HttpResponse response, CancellationToken cancellationToken) =>
    {
        var file = await ((ITusReadableStore)store).GetFileAsync(fileId, cancellationToken);

        if (file is null)
        {
            return Results.NotFound();
        }

        var metadata = await file.GetMetadataAsync(cancellationToken);

        var fileName = metadata.TryGetValue("filename", out var name) ? name.GetString(Encoding.UTF8) : fileId;

        var contentType = metadata.TryGetValue("filetype", out var type)
            ? type.GetString(Encoding.UTF8)
            : MediaTypeNames.Application.Octet;

        // FileNameStar carries non-ASCII names (RFC 8187); SetHttpFileName handles the fallback.
        var contentDisposition = new ContentDispositionHeaderValue("attachment");
        contentDisposition.SetHttpFileName(fileName);
        response.Headers[HeaderNames.ContentDisposition] = contentDisposition.ToString();

        var content = await file.GetContentAsync(cancellationToken);

        return Results.Stream(content, contentType);
    }
);

// Serve the built React SPA when frontend/ has been built into wwwroot (npm run build).
var indexHtml = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");

if (File.Exists(indexHtml))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}
else
{
    app.MapGet(
        "/",
        () =>
            "TUS demo backend is running. Build the frontend (cd frontend && npm install && npm run build) "
            + "and restart, or run it in dev mode (npm run dev) at http://localhost:5173."
    );
}

await app.RunAsync();
