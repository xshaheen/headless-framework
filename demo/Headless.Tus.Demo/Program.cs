using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Tus.Options;
using Headless.Tus.Services;
using Microsoft.Extensions.Azure;
using tusdotnet;
using tusdotnet.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddBlobServiceClient("CONNECTION_STRING").WithVersion(BlobClientOptions.ServiceVersion.V2024_08_04);
});

var app = builder.Build();

app.MapTus(
    "file-upload",
    httpContext =>
    {
        var blobServiceClient = httpContext.RequestServices.GetRequiredService<BlobServiceClient>();
        var tusAzureStoreOptions = new TusAzureStoreOptions { ContainerPublicAccessType = PublicAccessType.Blob };

        var tusConfiguration = new DefaultTusConfiguration
        {
            Store = new TusAzureStore(blobServiceClient, tusAzureStoreOptions),
            // For multi-node deployments, register Headless.Tus.DistributedLocks and set
            // FileLockProvider = new DistributedLockTusLockProvider(distributedLock). Omitting it uses
            // tusdotnet's in-process lock, which is sufficient for this single-node demo.
            UsePipelinesIfAvailable = true,
            AllowedExtensions = TusExtensions.All,
        };

        return Task.FromResult(tusConfiguration);
    }
);

app.MapGet("/", () => "Hello World!");

await app.RunAsync();
