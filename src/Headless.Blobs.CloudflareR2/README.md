# Headless.Blobs.CloudflareR2

Cloudflare R2 implementation of the `IBlobStorage` interface, running R2 as a private, S3-compatible blob backend.

## Problem Solved

R2 speaks the S3 API, but the AWS provider cannot be pointed at it directly: the endpoint, path-style addressing, and AWS SDK v4 checksum defaults all need R2-specific configuration, and R2 has no ACL concept. This package reuses the AWS S3 engine behind an R2-tuned client so R2 works as a drop-in, cost-saving replacement for S3.

## Key Features

- Full `IBlobStorage` implementation for Cloudflare R2 (reuses the AWS S3 engine)
- Presigned download/upload URLs via `IPresignedUrlBlobStorage`
- R2-correct client config: path-style addressing, `auto` region, and SDK v4 checksum settings R2 accepts
- R2 bucket naming normalization (no dots)
- Jurisdiction-aware endpoints (default, EU, FedRAMP)

## Design Notes

- **Buckets are not auto-created.** R2 API tokens are commonly scoped to object operations and cannot create buckets, so `AutoCreateContainer` defaults to `false`. Pre-create buckets out of band (the Cloudflare dashboard or an account-scoped token), or call `CreateContainerAsync` with a token that has bucket-create permission.
- **No ACLs / public access.** R2 has no per-object ACLs (`CannedAcl` is `null`). Public serving is a custom-domain / `r2.dev` concern and is out of scope for this provider; use presigned URLs for time-limited private access.
- **Single PUT is capped at ~5 GiB**, the same as S3.

## Installation

```bash
dotnet add package Headless.Blobs.CloudflareR2
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCloudflareR2BlobStorage(options =>
{
    options.AccountId = builder.Configuration["R2:AccountId"]!;
    options.AccessKeyId = builder.Configuration["R2:AccessKeyId"]!;
    options.SecretAccessKey = builder.Configuration["R2:SecretAccessKey"]!;
    // options.Jurisdiction = R2Jurisdiction.EuropeanUnion; // optional
});
```

Container and blob names are passed per operation, not configured here:

```csharp
await storage.UploadAsync(["my-bucket"], "reports/q1.pdf", stream);

// Time-limited delegated access to a private object:
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

Bind with `builder.Services.AddCloudflareR2BlobStorage(builder.Configuration.GetSection("R2"))`.

## Dependencies

- `Headless.Blobs.Aws` (the reused S3 engine)
- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `AWSSDK.S3`

## Side Effects

- Registers `IAmazonS3` (configured for R2) if not already registered
- Configures the shared `AwsBlobStorageOptions` with R2-safe defaults
- Registers `IBlobStorage` (the AWS S3 engine) as singleton
- Registers `IBlobNamingNormalizer` (R2 rules) as singleton
