# Headless.Blobs.Aws

AWS S3 implementation of `IBlobStorage` for storing files in Amazon S3.

## Problem Solved

Provides integration with AWS S3 for blob storage using the unified `IBlobStorage` abstraction, with per-store S3 client construction, presigned URL support, and opt-in bucket auto-create.

## Key Features

- Full `IBlobStorage` implementation for AWS S3.
- Bulk upload/delete with optimized batching.
- Two-tier name normalization: bucket name normalized to S3 rules; object-key path segments validated and preserved.
- Metadata support.
- Presigned download/upload URLs via `IPresignedUrlBlobStorage` (named stores only; feature-detect via cast for the default store).
- Opt-in, cached bucket auto-create (`AutoCreateContainer`, default `true`).
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

Buckets and keys are passed per operation:

```csharp
await storage.UploadAsync(["my-bucket"], "reports/q1.pdf", stream);

// Feature-detect presigned on the default store:
if (storage is IPresignedUrlBlobStorage presigned)
    var url = await presigned.GetPresignedDownloadUrlAsync(["my-bucket"], "file.pdf", TimeSpan.FromHours(1));
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
options.AutoCreateContainer = true; // create buckets on upload/copy (default true)
options.CannedAcl = S3CannedACL.Private;
options.UseChunkEncoding = true;
options.DisablePayloadSigning = false;
options.MaxBulkParallelism = 10;
```

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `AWSSDK.S3`
- `AWSSDK.Extensions.NETCore.Setup`

## Side Effects

Registered via `AddHeadlessBlobs(b => b.UseAws(...))` or `AddNamed("name", i => i.UseAws(...))`:

- Default (`UseAws`): registers `IBlobStorage` as unkeyed singleton. The per-store `IAmazonS3` is constructed inline; it is not registered in the DI container.
- Named (`AddNamed ... UseAws`): registers `IBlobStorage` as keyed singleton (`name`); registers `IPresignedUrlBlobStorage` as keyed singleton (`name`, forwarded from the keyed `IBlobStorage`). The per-store `IAmazonS3` is constructed inline.
