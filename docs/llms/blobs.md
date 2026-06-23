---
domain: Blob Storage
packages: Blobs.Abstractions, Blobs.Aws, Blobs.Azure, Blobs.CloudflareR2, Blobs.FileSystem, Blobs.Redis, Blobs.SshNet
---

# Blob Storage

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Blobs.Abstractions](#headlessblobsabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Blobs.Aws](#headlessblobsaws)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Blobs.Azure](#headlessblobsazure)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Blobs.FileSystem](#headlessblobsfilesystem)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Blobs.Redis](#headlessblobsredis)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Design Notes](#design-notes)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.Blobs.SshNet](#headlessblobssshnet)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)
- [Headless.Blobs.CloudflareR2](#headlessblobscloudflarer2)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-6)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-6)

> Provider-agnostic file/blob storage with implementations for AWS S3, Azure Blob, Cloudflare R2, local filesystem, Redis, and SFTP.

## Quick Orientation

Install `Headless.Blobs.Abstractions` plus exactly one provider package. Code against `IBlobStorage` — never reference concrete provider types in application code. See [Choosing a Provider](#choosing-a-provider) for the selection table. All providers register `IBlobStorage` as a singleton; container paths are arrays of strings (e.g., `["uploads", "images"]`), not slash-delimited strings.

## Agent Instructions

- Always depend on `IBlobStorage` from `Headless.Blobs.Abstractions` — never reference `AwsS3BlobStorage`, `AzureBlobStorage`, or other concrete types in service code.
- Use `Blobs.FileSystem` (`AddFileSystemBlobStorage()`) for local development and testing. Use `Blobs.Aws`, `Blobs.Azure`, or `Blobs.CloudflareR2` for production.
- Presigned URLs are an opt-in capability: `IPresignedUrlBlobStorage` (presigned GET + PUT) is implemented only by AWS, Cloudflare R2, and Azure (SAS). Feature-detect with `storage is IPresignedUrlBlobStorage` — FileSystem, Redis, and SshNet do not implement it. Azure signs locally with an account-key client and falls back to a user-delegation SAS for AAD/`DefaultAzureCredential` clients (the identity needs the `Storage Blob Delegator` role plus a data role; the URL is capped at 7 days); only a bare-SAS-token or anonymous client throws at call time.
- Container auto-create on upload/copy is opt-in and cached once per container per instance. The S3 engine (AWS and R2) uses `AwsBlobStorageOptions.AutoCreateContainer` (AWS default `true`, R2 default `false`); Azure uses `AzureStorageOptions.AutoCreateContainer` (default `true`). With it off, pre-create containers or call `CreateContainerAsync`. The S3 engine also no longer pre-checks the bucket on read/exists/delete.
- Redis blob storage (`Blobs.Redis`) is for small blobs only (metadata, thumbnails, temporary uploads). The default `MaxBlobSizeBytes` is 10 MB. For large files, use S3 or Azure.
- Always dispose the result of `OpenReadStreamAsync()` promptly — holding it may exhaust connection pools. Use `await using`.
- Container paths are string arrays, not slash-delimited strings: `["uploads", "images"]` not `"uploads/images"`.
- Naming is normalized two-tier through each provider's `IBlobNamingNormalizer`: the **first** container segment is the backend bucket/container (strict backend rules — e.g. lowercase, length, allowed characters) and the **remaining** segments plus the blob name are the object path (lenient — validated, not rewritten). This is applied uniformly by every provider (AWS, R2, Azure, FileSystem, SshNet, Redis); do not manually normalize paths.
- Metadata is a `Dictionary<string, string?>`. For `FileSystem` provider, metadata is stored as companion JSON files. For `Redis`, metadata is stored alongside blobs in Redis.
- `SshNet` accepts a `ConnectionString` (SSH URI) for both password and key-based auth. For key-based auth, set `PrivateKey` (a `Stream`) and optionally `PrivateKeyPassPhrase`.
- Only one provider can be the default `IBlobStorage` registration. If you need multiple providers, use keyed services or named registrations.

## Choosing a Provider

Pick one provider per `IBlobStorage` registration based on where the bytes must live and which capabilities you need.

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Blobs.FileSystem` | Local dev, testing, or single-node on-prem with no cloud dependency | Multi-node or horizontally-scaled deployments (no shared storage) | Not distributed; metadata kept as companion JSON files |
| `Headless.Blobs.Aws` | Production on AWS; need presigned URLs and bulk operations | Not on AWS, or egress cost is a concern | Ties you to S3 pricing and the AWS SDK |
| `Headless.Blobs.CloudflareR2` | S3-compatible storage with low egress cost and private buckets | You need public serving via ACLs, or bucket auto-create from the app | No ACL concept; buckets must be pre-created (no auto-create) |
| `Headless.Blobs.Azure` | Production on Azure; want Entra ID auth and SAS presigned URLs | Not on Azure | Requires a pre-registered `BlobServiceClient`; extra SAS rules for AAD clients |
| `Headless.Blobs.SshNet` | Files must land on a remote SFTP/SSH server or legacy system | High-throughput or presigned-URL workloads | Slower; no presigned URLs; opens live SSH connections |
| `Headless.Blobs.Redis` | Small, ephemeral blobs (thumbnails, temp uploads) needing fast access | Large files (default 10 MB cap) or durable storage | In-memory cost; not for large or long-lived blobs |

---

## Headless.Blobs.Abstractions

Defines the unified interface for blob/file storage operations across different providers (AWS S3, Azure Blob, FileSystem, Redis, SFTP).

### Problem Solved

Provides a provider-agnostic API for file storage operations, so application code can switch between cloud providers or local storage without changing.

### Key Features

- `IBlobStorage` - Core interface for all storage operations:
  - Upload blobs with metadata
  - Open read stream for downloading
  - Bulk upload/delete operations
  - Copy/Rename/Delete operations
  - Exists check and blob info retrieval
  - Paged listing with search patterns
- `IBlobNamingNormalizer` - Provider-specific path normalization
- Container/directory management
- Metadata support

### Installation

```bash
dotnet add package Headless.Blobs.Abstractions
```

### Quick Start

```csharp
public sealed class FileService(IBlobStorage storage)
{
    public async Task UploadAsync(Stream file, string fileName, CancellationToken ct)
    {
        await storage.UploadAsync(
            container: ["uploads", "images"],
            blobName: fileName,
            stream: file,
            metadata: new Dictionary<string, string?> { ["uploaded-by"] = "user-123" },
            cancellationToken: ct
        );
    }

    public async Task<string?> GetContentAsync(string fileName, CancellationToken ct)
    {
        // IMPORTANT: Dispose result promptly - holding it may exhaust connection pools
        await using var result = await storage.OpenReadStreamAsync(["uploads", "images"], fileName, ct);
        if (result is null) return null;

        using var reader = new StreamReader(result.Stream);
        return await reader.ReadToEndAsync(ct);
    }
}
```

### Configuration

No configuration required. This is an abstractions-only package.

### Dependencies

- `Headless.Extensions`
- `Headless.Serializer.Json`

### Side Effects

None. This is an abstractions package.

---

## Headless.Blobs.Aws

AWS S3 implementation of the `IBlobStorage` interface for storing files in Amazon S3.

### Problem Solved

Provides AWS S3 integration for blob storage using the unified `IBlobStorage` abstraction.

### Key Features

- Full `IBlobStorage` implementation for AWS S3
- Bulk upload/delete with optimized batching
- Two-tier name normalization: the bucket name is normalized to S3 rules; object-key path segments are validated and preserved
- Metadata support
- Presigned download/upload URLs via `IPresignedUrlBlobStorage`
- Opt-in, cached bucket auto-create (`AutoCreateContainer`)
- Integration with AWS SDK configuration

### Installation

```bash
dotnet add package Headless.Blobs.Aws
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: Use configuration-based AWS options
var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddAwsS3BlobStorage(awsOptions);

// Option 2: Manual configuration
builder.Services.AddAwsS3BlobStorage(new AWSOptions
{
    Region = RegionEndpoint.USEast1,
    Credentials = new BasicAWSCredentials("access-key", "secret-key")
});
```

Buckets and keys are passed per operation, not configured at registration:

```csharp
await storage.UploadAsync(["my-bucket"], "reports/q1.pdf", stream);
```

### Configuration

#### appsettings.json

```json
{
  "AWS": {
    "Region": "us-east-1",
    "AccessKey": "your-access-key",
    "SecretKey": "your-secret-key"
  }
}
```

#### Options

```csharp
options.AutoCreateContainer = true; // create buckets on upload/copy (default true; set false for R2)
options.CannedAcl = S3CannedACL.Private;
options.UseChunkEncoding = true;
options.DisablePayloadSigning = false;
options.MaxBulkParallelism = 10;
```

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `AWSSDK.S3`
- `AWSSDK.Extensions.NETCore.Setup`

### Side Effects

- Registers `IAmazonS3` if not already registered
- Registers `IBlobStorage` as singleton
- Registers `IBlobNamingNormalizer` as singleton

---

## Headless.Blobs.Azure

Azure Blob Storage implementation of the `IBlobStorage` interface for storing files in Azure.

### Problem Solved

Provides Azure Blob Storage integration using the unified `IBlobStorage` abstraction.

### Key Features

- Full `IBlobStorage` implementation for Azure Blob Storage
- Bulk operations with Azure Batch API
- Opt-in, cached container auto-create (`AutoCreateContainer`)
- Metadata support
- Presigned download/upload URLs via `IPresignedUrlBlobStorage` (SAS-based)
- Integration with Azure.Identity for authentication

### Installation

```bash
dotnet add package Headless.Blobs.Azure
```

### Quick Start

Register a `BlobServiceClient` in DI first, then call `AddAzureBlobStorage`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: connection string
builder.Services.AddSingleton(new BlobServiceClient(
    builder.Configuration["Azure:Storage:ConnectionString"]));

// Option 2: Azure.Identity (DefaultAzureCredential)
builder.Services.AddSingleton(new BlobServiceClient(
    new Uri($"https://mystorageaccount.blob.core.windows.net"),
    new DefaultAzureCredential()));

// Option 3: Aspire integration
// builder.AddAzureBlobClient("blobs");

builder.Services.AddAzureBlobStorage(options =>
{
    options.AutoCreateContainer = true; // default
});
```

### Configuration

#### appsettings.json

```json
{
  "Azure": {
    "Storage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
    }
  }
}
```

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `Azure.Storage.Blobs`
- `Azure.Storage.Blobs.Batch`
- `Microsoft.Extensions.Azure`

### Side Effects

- Requires a `BlobServiceClient` to be registered in DI before this call
- Registers `IBlobStorage` as singleton
- Registers `IBlobNamingNormalizer` as singleton

---

## Headless.Blobs.FileSystem

Local file system implementation of the `IBlobStorage` interface for development and on-premises scenarios.

### Problem Solved

Provides local file system storage using the unified `IBlobStorage` abstraction, ideal for development, testing, and on-premises deployments without cloud dependencies.

### Key Features

- Full `IBlobStorage` implementation using local file system
- Container mapping to directories
- Metadata stored as companion JSON files
- No external service dependencies
- Cross-platform path handling

### Installation

```bash
dotnet add package Headless.Blobs.FileSystem
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFileSystemBlobStorage(options =>
{
    options.BaseDirectoryPath = Path.Combine(builder.Environment.ContentRootPath, "storage");
});
```

### Configuration

#### appsettings.json

```json
{
  "FileSystemBlob": {
    "BaseDirectoryPath": "/var/data/blobs"
  }
}
```

#### Options

```csharp
options.BaseDirectoryPath = "/path/to/storage";
```

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Hosting`

### Side Effects

- Registers `IBlobStorage` as singleton
- Creates the base directory if it doesn't exist

---

## Headless.Blobs.Redis

Redis implementation of the `IBlobStorage` interface for caching small blobs in Redis.

### Problem Solved

Provides high-speed blob storage for small files using Redis, suitable for temporary files, cache data, or session-related binary content.

### Key Features

- Full `IBlobStorage` implementation using Redis
- Suitable for small-to-medium sized blobs
- Fast read/write performance
- Automatic key expiration support
- Metadata stored alongside blobs

### Design Notes

- Designed for small, ephemeral blobs (cache data, session files, temporary uploads). The default `MaxBlobSizeBytes` is 10 MB to prevent memory exhaustion; uploads above the cap are rejected. For large files, use Azure Blob Storage or S3.

### Installation

```bash
dotnet add package Headless.Blobs.Redis
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var multiplexer = ConnectionMultiplexer.Connect("localhost:6379");
builder.Services.AddRedisBlobStorage(options =>
{
    options.ConnectionMultiplexer = multiplexer;
});
```

### Configuration

`RedisBlobStorageOptions` requires an `IConnectionMultiplexer` instance; the options cannot be bound from `appsettings.json` directly. Wire up the multiplexer in code as shown in Quick Start.

| Option | Default | Description |
|--------|---------|-------------|
| `ConnectionMultiplexer` | *(required)* | `IConnectionMultiplexer` instance for Redis. |
| `MaxBlobSizeBytes` | 10 MB | Maximum blob size in bytes. Set to 0 to disable. |
| `MaxBulkParallelism` | 10 | Maximum parallelism for bulk operations. |

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `StackExchange.Redis`

### Side Effects

- Registers `IBlobStorage` as singleton
- Requires an `IConnectionMultiplexer` to be provided via `RedisBlobStorageOptions.ConnectionMultiplexer`

---

## Headless.Blobs.SshNet

SFTP/SSH implementation of the `IBlobStorage` interface for storing files on remote servers via SFTP.

### Problem Solved

Provides blob storage via SFTP/SSH protocol for scenarios requiring file transfer to remote servers, legacy system integration, or secure file exchange.

### Key Features

- Full `IBlobStorage` implementation using SFTP
- SSH key and password authentication
- Remote directory management
- Metadata support via companion files
- Connection pooling

### Installation

```bash
dotnet add package Headless.Blobs.SshNet
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Password authentication
builder.Services.AddSshBlobStorage(options =>
{
    options.ConnectionString = "sftp://user:password@sftp.example.com:22/home/user/uploads";
});
```

### Configuration

#### appsettings.json

```json
{
  "SftpBlob": {
    "ConnectionString": "sftp://user:password@sftp.example.com:22/home/user/uploads"
  }
}
```

#### SSH Key Authentication

```csharp
// Key-based authentication: provide PrivateKey as a Stream
builder.Services.AddSshBlobStorage(options =>
{
    options.ConnectionString = "sftp://user@sftp.example.com:22/home/user/uploads";
    options.PrivateKey = File.OpenRead("/path/to/key");
    options.PrivateKeyPassPhrase = "optional-passphrase"; // nullable
});
```

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Hosting`
- `SSH.NET`

### Side Effects

- Registers `IBlobStorage` as singleton
- Opens SSH/SFTP connections to remote server

---

## Headless.Blobs.CloudflareR2

Cloudflare R2 implementation of `IBlobStorage`, running R2 as a private, S3-compatible blob backend on the reused AWS S3 engine.

### Problem Solved

R2 speaks the S3 API but cannot use the AWS provider as-is: the endpoint, path-style addressing, and AWS SDK v4 checksum defaults need R2-specific configuration, and R2 has no ACL concept. This package configures an R2-tuned `IAmazonS3` and reuses `AwsBlobStorage`, making R2 a drop-in, cost-saving S3 replacement.

### Key Features

- Full `IBlobStorage` implementation for Cloudflare R2 (reuses the AWS S3 engine)
- Presigned download/upload URLs via `IPresignedUrlBlobStorage`
- R2-correct client config: path-style addressing, `auto` region, SDK v4 checksum settings R2 accepts
- R2 bucket naming normalization (no dots)
- Jurisdiction-aware endpoints (default, EU, FedRAMP)

### Design Notes

- **Buckets are not auto-created.** R2 API tokens are commonly scoped to object operations and cannot create buckets, so `AutoCreateContainer` defaults to `false`. Pre-create buckets out of band (the Cloudflare dashboard or an account-scoped token), or call `CreateContainerAsync` with a token that has bucket-create permission.
- **No ACLs / public access.** R2 has no per-object ACLs (`CannedAcl` is `null`). Public serving is a custom-domain / `r2.dev` concern and is out of scope for this provider; use presigned URLs for time-limited private access.
- **Single PUT is capped at ~5 GiB**, the same as S3.

### Installation

```bash
dotnet add package Headless.Blobs.CloudflareR2
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCloudflareR2BlobStorage(options =>
{
    options.AccountId = builder.Configuration["R2:AccountId"]!;
    options.AccessKeyId = builder.Configuration["R2:AccessKeyId"]!;
    options.SecretAccessKey = builder.Configuration["R2:SecretAccessKey"]!;
    // options.Jurisdiction = R2Jurisdiction.EuropeanUnion; // optional
});

// Buckets and keys are passed per operation:
await storage.UploadAsync(["my-bucket"], "reports/q1.pdf", stream);
```

### Configuration

#### appsettings.json

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

### Dependencies

- `Headless.Blobs.Aws`
- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `AWSSDK.S3`

### Side Effects

- Registers `IAmazonS3` (configured for R2) if not already registered
- Configures the shared `AwsBlobStorageOptions` with R2-safe defaults
- Registers `IBlobStorage` (the AWS S3 engine) as singleton
- Registers `IBlobNamingNormalizer` (R2 rules) as singleton
