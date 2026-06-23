# Headless.Blobs.CloudflareR2

Cloudflare R2 implementation of `IBlobStorage`, running R2 as a private, S3-compatible blob backend on the reused AWS S3 engine.

## Problem Solved

R2 speaks the S3 API but cannot use the AWS provider as-is: the endpoint, path-style addressing, and AWS SDK v4 checksum defaults need R2-specific configuration, and R2 has no ACL concept. This package configures an R2-tuned `IAmazonS3` via `R2ClientFactory` and reuses `AwsBlobStorage`, making R2 a drop-in, cost-saving S3 replacement.

## Key Features

- Full `IBlobStorage` implementation for Cloudflare R2 (reuses the AWS S3 engine).
- Presigned download/upload URLs via `IPresignedUrlBlobStorage` (named stores only — feature-detect via cast for the default store).
- R2-correct client config: path-style addressing, `auto` region, SDK v4 checksum settings R2 accepts.
- R2 bucket naming normalization (no dots).
- Jurisdiction-aware endpoints (default, EU, FedRAMP).
- R2-safe defaults applied per named instance (`AwsBlobStorageOptions`): `CannedAcl = null`, `UseChunkEncoding = false`, `DisablePayloadSigning = true`, `AutoCreateContainer = false`.

## Installation

```bash
dotnet add package Headless.Blobs.CloudflareR2
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
{
    // Default store.
    blobs.UseCloudflareR2(options =>
    {
        options.AccountId = builder.Configuration["R2:AccountId"]!;
        options.AccessKeyId = builder.Configuration["R2:AccessKeyId"]!;
        options.SecretAccessKey = builder.Configuration["R2:SecretAccessKey"]!;
        // options.Jurisdiction = R2Jurisdiction.EuropeanUnion; // optional
    });

    // Named store — keyed IPresignedUrlBlobStorage("media") registered automatically.
    blobs.AddNamed("media", instance => instance.UseCloudflareR2(options =>
    {
        options.AccountId = builder.Configuration["R2Media:AccountId"]!;
        options.AccessKeyId = builder.Configuration["R2Media:AccessKeyId"]!;
        options.SecretAccessKey = builder.Configuration["R2Media:SecretAccessKey"]!;
    }));
});
```

Container and blob names are passed per operation:

```csharp
await storage.UploadAsync(["my-bucket"], "reports/q1.pdf", stream);

// Feature-detect presigned on the default store:
if (storage is IPresignedUrlBlobStorage presigned)
{
    var url = await presigned.GetPresignedDownloadUrlAsync(["my-bucket"], "reports/q1.pdf", TimeSpan.FromMinutes(15));
}
```

## Configuration

### appsettings.json

```json
{
  "R2": {
    "AccountId": "your-account-id",
    "AccessKeyId": "your-access-key-id",
    "SecretAccessKey": "your-secret-access-key",
    "Jurisdiction": "Default"
  }
}
```

Bind with `blobs.UseCloudflareR2(builder.Configuration.GetSection("R2"))`.

## Behavior Notes

- **Buckets are not auto-created.** R2 object-scoped tokens cannot create buckets, so `AutoCreateContainer` defaults to `false`. Pre-create buckets out of band or use a bucket-create-capable token with `CreateContainerAsync`.
- **No ACLs / public access.** `CannedAcl` is `null`. Use presigned URLs for time-limited private access; public serving (custom domains / `r2.dev`) is out of scope.
- **Single PUT is capped at ~5 GiB**, the same as S3.

## Dependencies

- `Headless.Blobs.Aws` (the reused S3 engine)
- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `AWSSDK.S3`

## Side Effects

Registered via `AddHeadlessBlobs(b => b.UseCloudflareR2(...))` or `AddNamed("name", i => i.UseCloudflareR2(...))`:

- Default (`UseCloudflareR2`): registers `IBlobStorage` as unkeyed singleton. The per-store `IAmazonS3` (R2-tuned) is constructed inline; it is not registered in the DI container.
- Named (`AddNamed ... UseCloudflareR2`): configures named `AwsBlobStorageOptions` with R2 forced defaults; registers `IBlobStorage` as keyed singleton (`name`); registers `IPresignedUrlBlobStorage` as keyed singleton (`name`, forwarded from the keyed `IBlobStorage`). The per-store `IAmazonS3` is constructed inline.
