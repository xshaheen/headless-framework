# Headless.Blobs.Aws

AWS S3 implementation of `IBlobStorage` for storing files in Amazon S3.

## Problem Solved

Provides integration with AWS S3 for blob storage using the unified `IBlobStorage` abstraction, with per-store S3 client construction, presigned URL support, and an opt-in bucket-lifecycle capability.

## Key Features

- Full `IBlobStorage` implementation for AWS S3, routed through the shared resolve seam.
- Bulk upload/delete with optimized batching, returning identity-carrying `BlobBulkResult` lists.
- Native-token paging: `ListAsync` uses the S3 `ListObjectsV2` continuation token as the opaque `BlobPage` token.
- Two-tier name normalization: bucket name normalized to S3 rules; object-key path segments validated and preserved.
- Metadata support; `GetBlobInfoAsync` reads metadata from the HEAD response. (The list API omits per-object metadata, and its `Created` falls back to `LastModified`.)
- Presigned download/upload URLs over a `BlobLocation` via `IPresignedUrlBlobStorage` (named stores only; feature-detect via cast for the default store).
- Bucket lifecycle via a dedicated `AwsBlobContainerManager` resolved from DI (`EnsureContainerAsync` keeps a per-instance ensured-bucket cache). `UploadAsync` no longer auto-creates a missing bucket — that is an error.
- Per-store `IAmazonS3` constructed via `S3ClientFactory`; optional `AWSOptions` to override the SDK credential/region chain.

## Installation

```bash
dotnet add package Headless.Blobs.Aws
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Default store — AWS SDK credential/region chain applies unless overridden.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseAws(options => { }, awsOptions: builder.Configuration.GetAWSOptions())
);

// Explicit credentials:
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseAws(
        options => { },
        new AWSOptions
        {
            Region = RegionEndpoint.USEast1,
            Credentials = new BasicAWSCredentials("access-key", "secret-key"),
        }
    )
);

// Named store with per-store credentials; keyed IPresignedUrlBlobStorage registered automatically.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.AddNamed(
        "archive",
        instance => instance.UseAws(options => { }, awsOptions: builder.Configuration.GetAWSOptions("AWS:Archive"))
    )
);
```

Blobs are addressed by `BlobLocation`; the bucket must already exist:

```csharp
var location = new BlobLocation("my-bucket", "reports/q1.pdf");

// Provision the bucket first (resolved from DI; not a cast from the store):
var manager = serviceProvider.GetService<IBlobContainerManager>();
if (manager is not null)
    await manager.EnsureContainerAsync("my-bucket");

await storage.UploadAsync(location, stream);

// Presigned URL on the default store — feature-detect:
if (storage is IPresignedUrlBlobStorage presigned)
{
    var url = await presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.FromHours(1));
}
```

## Configuration

### appsettings.json

```json
{
  "AWS": {
    "Region": "us-east-1",
    "AccessKey": "your-access-key",
    "SecretKey": "your-secret-key"
  }
}
```

### Options

```csharp
options.CannedAcl = S3CannedACL.Private;
options.UseChunkEncoding = true;
options.DisablePayloadSigning = false;
options.MaxBulkParallelism = 10;
// options.AutoCreateContainer — legacy flag, retained for option-shape compatibility; no longer consulted by the
// write path (UploadAsync never auto-creates a bucket). Use IBlobContainerManager.EnsureContainerAsync instead.
```

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Blobs.Core`
- `Headless.Core`
- `Headless.Hosting`
- `AWSSDK.S3`
- `AWSSDK.Extensions.NETCore.Setup`

## Side Effects

Registered via `AddHeadlessBlobs(b => b.UseAws(...))` or `AddNamed("name", i => i.UseAws(...))`:

- Default (`UseAws`): registers `IBlobStorage` as unkeyed singleton and `IBlobContainerManager` as unkeyed singleton (`AwsBlobContainerManager`). The per-store `IAmazonS3` is constructed inline; it is not registered in the DI container.
- Named (`AddNamed ... UseAws`): registers `IBlobStorage`, `IPresignedUrlBlobStorage` (forwarded from the keyed `IBlobStorage`), and `IBlobContainerManager` each as keyed singleton (`name`). The per-store `IAmazonS3` is constructed inline.
