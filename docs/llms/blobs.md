---
domain: Blob Storage
packages: Blobs.Abstractions, Blobs.Aws, Blobs.Azure, Blobs.CloudflareR2, Blobs.FileSystem, Blobs.Redis, Blobs.SshNet
---

# Blob Storage

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Blobs.Abstractions](#headlessblobsabstractions)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation)
  - [Usage](#usage)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.Blobs.Aws](#headlessblobsaws)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start)
  - [Configuration](#configuration-1)
    - [appsettings.json](#appsettingsjson)
    - [Options](#options)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.Blobs.Azure](#headlessblobsazure)
  - [Problem Solved](#problem-solved-2)
  - [Key Features](#key-features-2)
  - [Installation](#installation-2)
  - [Quick Start](#quick-start-1)
  - [Configuration](#configuration-2)
    - [appsettings.json](#appsettingsjson-1)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)
- [Headless.Blobs.FileSystem](#headlessblobsfilesystem)
  - [Problem Solved](#problem-solved-3)
  - [Key Features](#key-features-3)
  - [Installation](#installation-3)
  - [Quick Start](#quick-start-2)
  - [Configuration](#configuration-3)
    - [appsettings.json](#appsettingsjson-2)
    - [Options](#options-1)
  - [Dependencies](#dependencies-3)
  - [Side Effects](#side-effects-3)
- [Headless.Blobs.Redis](#headlessblobsredis)
  - [Problem Solved](#problem-solved-4)
  - [Key Features](#key-features-4)
  - [Installation](#installation-4)
  - [Quick Start](#quick-start-3)
  - [Configuration](#configuration-4)
    - [appsettings.json](#appsettingsjson-3)
    - [Options](#options-2)
  - [Usage Notes](#usage-notes)
  - [Dependencies](#dependencies-4)
  - [Side Effects](#side-effects-4)
- [Headless.Blobs.SshNet](#headlessblobssshnet)
  - [Problem Solved](#problem-solved-5)
  - [Key Features](#key-features-5)
  - [Installation](#installation-5)
  - [Quick Start](#quick-start-4)
  - [Configuration](#configuration-5)
    - [appsettings.json](#appsettingsjson-4)
    - [SSH Key Authentication](#ssh-key-authentication)
  - [Dependencies](#dependencies-5)
  - [Side Effects](#side-effects-5)
- [Headless.Blobs.CloudflareR2](#headlessblobscloudflarer2)
  - [Problem Solved](#problem-solved-6)
  - [Key Features](#key-features-6)
  - [Installation](#installation-6)
  - [Quick Start](#quick-start-5)
  - [Configuration](#configuration-6)
    - [appsettings.json](#appsettingsjson-5)
  - [Behavior Notes](#behavior-notes)
  - [Dependencies](#dependencies-6)
  - [Side Effects](#side-effects-6)

> Provider-agnostic file/blob storage with implementations for AWS S3, Azure Blob, local filesystem, Redis, and SFTP.

## Quick Orientation

Install `Headless.Blobs.Abstractions` plus one provider package. Code against `IBlobStorage` — never reference concrete provider types in application code.

Provider selection guide:
- **Development/testing**: `Headless.Blobs.FileSystem` — no external dependencies, stores files on disk.
- **Production (AWS)**: `Headless.Blobs.Aws` — full S3 integration with bulk operations and presigned URLs.
- **Production (Azure)**: `Headless.Blobs.Azure` — Azure Blob Storage with Batch API and Azure.Identity support.
- **Production (Cloudflare R2)**: `Headless.Blobs.CloudflareR2` — private, S3-compatible storage on the reused AWS engine; a cost-saving S3 replacement.
- **SFTP/legacy**: `Headless.Blobs.SshNet` — SFTP protocol for remote servers and legacy system integration.
- **Small cached blobs**: `Headless.Blobs.Redis` — Redis-backed storage for small, ephemeral blobs only (default 10 MB limit).

All providers register `IBlobStorage` as singleton. Container paths are arrays of strings (e.g., `["uploads", "images"]`).

## Agent Instructions

- Always depend on `IBlobStorage` from `Headless.Blobs.Abstractions` — never reference `AwsS3BlobStorage`, `AzureBlobStorage`, or other concrete types in service code.
- Use `Blobs.FileSystem` (`AddFileSystemBlobStorage()`) for local development and testing. Use `Blobs.Aws`, `Blobs.Azure`, or `Blobs.CloudflareR2` for production.
- Presigned URLs are an opt-in capability: `IPresignedUrlBlobStorage` (presigned GET + PUT) is implemented only by AWS, Cloudflare R2, and Azure (SAS). Feature-detect with `storage is IPresignedUrlBlobStorage` — FileSystem, Redis, and SshNet do not implement it. Azure can throw at call time if its `BlobServiceClient` was wired without account-key/user-delegation-key credentials.
- Container auto-create on upload/copy is opt-in and cached once per container per instance. The S3 engine (AWS and R2) uses `AwsBlobStorageOptions.AutoCreateContainer` (AWS default `true`, R2 default `false`); Azure uses `AzureStorageOptions.AutoCreateContainer` (default `true`). With it off, pre-create containers or call `CreateContainerAsync`. The S3 engine also no longer pre-checks the bucket on read/exists/delete.
- Redis blob storage (`Blobs.Redis`) is for small blobs only (metadata, thumbnails, temporary uploads). The default `MaxBlobSizeBytes` is 10 MB. For large files, use S3 or Azure.
- Always dispose the result of `OpenReadStreamAsync()` promptly — holding it may exhaust connection pools. Use `await using`.
- Container paths are string arrays, not slash-delimited strings: `["uploads", "images"]` not `"uploads/images"`.
- Naming is normalized two-tier through each provider's `IBlobNamingNormalizer`: the **first** container segment is the backend bucket/container (strict backend rules — e.g. lowercase, length, allowed characters) and the **remaining** segments plus the blob name are the object path (lenient — validated, not rewritten). This is applied uniformly by every provider (AWS, R2, Azure, FileSystem, SshNet, Redis); do not manually normalize paths.
- Metadata is a `Dictionary<string, string?>`. For `FileSystem` provider, metadata is stored as companion JSON files. For `Redis`, metadata is stored alongside blobs in Redis.
- `SshNet` supports both password and SSH key authentication. Use `PrivateKeyPath` for key-based auth.
- Only one provider can be the default `IBlobStorage` registration. If you need multiple providers, use keyed services or named registrations.

---
# Headless.Blobs.Abstractions

Defines the unified interface for blob/file storage operations across different providers (AWS S3, Azure Blob, FileSystem, Redis, SFTP).

## Problem Solved

Provides a provider-agnostic API for file storage operations, enabling seamless switching between cloud providers or local storage without changing application code.

## Key Features

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

## Installation

```bash
dotnet add package Headless.Blobs.Abstractions
```

## Usage

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

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

- `Headless.Extensions`
- `Headless.Serializer.Json`

## Side Effects

None. This is an abstractions package.
---
# Headless.Blobs.Aws

AWS S3 implementation of the `IBlobStorage` interface for storing files in Amazon S3.

## Problem Solved

Provides seamless integration with AWS S3 for blob storage using the unified `IBlobStorage` abstraction.

## Key Features

- Full `IBlobStorage` implementation for AWS S3
- Bulk upload/delete with optimized batching
- Two-tier name normalization: the bucket name is normalized to S3 rules; object-key path segments are validated and preserved
- Metadata support
- Presigned download/upload URLs via `IPresignedUrlBlobStorage`
- Opt-in, cached bucket auto-create (`AutoCreateContainer`)
- Integration with AWS SDK configuration

## Installation

```bash
dotnet add package Headless.Blobs.Aws
```

## Quick Start

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
options.AutoCreateContainer = true; // create buckets on upload/copy (default true; set false for R2)
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

- Registers `IAmazonS3` if not already registered
- Registers `IBlobStorage` as singleton
- Registers `IBlobNamingNormalizer` as singleton
---
# Headless.Blobs.Azure

Azure Blob Storage implementation of the `IBlobStorage` interface for storing files in Azure.

## Problem Solved

Provides seamless integration with Azure Blob Storage using the unified `IBlobStorage` abstraction.

## Key Features

- Full `IBlobStorage` implementation for Azure Blob Storage
- Bulk operations with Azure Batch API
- Opt-in, cached container auto-create (`AutoCreateContainer`)
- Metadata support
- Presigned download/upload URLs via `IPresignedUrlBlobStorage` (SAS-based)
- Integration with Azure.Identity for authentication

## Installation

```bash
dotnet add package Headless.Blobs.Azure
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAzureBlobStorage(options =>
{
    options.ConnectionString = builder.Configuration["Azure:Storage:ConnectionString"];
    options.ContainerName = "my-container";
});

// Or with Azure.Identity
builder.Services.AddAzureBlobStorage(options =>
{
    options.AccountName = "mystorageaccount";
    options.ContainerName = "my-container";
    // Uses DefaultAzureCredential
});
```

## Configuration

### appsettings.json

```json
{
  "Azure": {
    "Storage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
      "ContainerName": "my-container"
    }
  }
}
```

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `Azure.Storage.Blobs`
- `Azure.Storage.Blobs.Batch`
- `Microsoft.Extensions.Azure`

## Side Effects

- Registers `BlobServiceClient` via Azure client factory
- Registers `IBlobStorage` as singleton
- Registers `IBlobNamingNormalizer` as singleton
---
# Headless.Blobs.FileSystem

Local file system implementation of the `IBlobStorage` interface for development and on-premises scenarios.

## Problem Solved

Provides local file system storage using the unified `IBlobStorage` abstraction, ideal for development, testing, and on-premises deployments without cloud dependencies.

## Key Features

- Full `IBlobStorage` implementation using local file system
- Container mapping to directories
- Metadata stored as companion JSON files
- No external service dependencies
- Cross-platform path handling

## Installation

```bash
dotnet add package Headless.Blobs.FileSystem
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFileSystemBlobStorage(options =>
{
    options.BasePath = Path.Combine(builder.Environment.ContentRootPath, "storage");
});
```

## Configuration

### appsettings.json

```json
{
  "FileSystemBlob": {
    "BasePath": "/var/data/blobs"
  }
}
```

### Options

```csharp
options.BasePath = "/path/to/storage";
```

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Hosting`

## Side Effects

- Registers `IBlobStorage` as singleton
- Creates the base directory if it doesn't exist
---
# Headless.Blobs.Redis

Redis implementation of the `IBlobStorage` interface for caching small blobs in Redis.

## Problem Solved

Provides high-speed blob storage for small files using Redis, suitable for temporary files, cache data, or session-related binary content.

## Key Features

- Full `IBlobStorage` implementation using Redis
- Suitable for small-to-medium sized blobs
- Fast read/write performance
- Automatic key expiration support
- Metadata stored alongside blobs

## Installation

```bash
dotnet add package Headless.Blobs.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRedisBlobStorage(options =>
{
    options.ConnectionString = "localhost:6379";
    options.KeyPrefix = "blobs:";
});
```

## Configuration

### appsettings.json

```json
{
  "RedisBlob": {
    "ConnectionString": "localhost:6379,password=secret",
    "KeyPrefix": "blobs:"
  }
}
```

### Options

| Option | Default | Description |
|--------|---------|-------------|
| `MaxBlobSizeBytes` | 10 MB | Maximum blob size. Set to 0 to disable. |
| `MaxBulkParallelism` | 10 | Maximum parallelism for bulk operations. |

## Usage Notes

**Size Limits:** Redis blob storage is designed for small, ephemeral blobs (cache data, session files, temporary uploads). The default 10 MB limit prevents memory exhaustion. For large files, use Azure Blob Storage or S3.

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `StackExchange.Redis`

## Side Effects

- Registers `IBlobStorage` as singleton
- Requires Redis connection (uses existing `IConnectionMultiplexer` if registered)
---
# Headless.Blobs.SshNet

SFTP/SSH implementation of the `IBlobStorage` interface for storing files on remote servers via SFTP.

## Problem Solved

Provides blob storage via SFTP/SSH protocol for scenarios requiring file transfer to remote servers, legacy system integration, or secure file exchange.

## Key Features

- Full `IBlobStorage` implementation using SFTP
- SSH key and password authentication
- Remote directory management
- Metadata support via companion files
- Connection pooling

## Installation

```bash
dotnet add package Headless.Blobs.SshNet
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSshNetBlobStorage(options =>
{
    options.Host = "sftp.example.com";
    options.Port = 22;
    options.Username = "user";
    options.Password = "password"; // Or use PrivateKeyPath
    options.BasePath = "/home/user/uploads";
});
```

## Configuration

### appsettings.json

```json
{
  "SftpBlob": {
    "Host": "sftp.example.com",
    "Port": 22,
    "Username": "user",
    "Password": "secret",
    "BasePath": "/home/user/uploads"
  }
}
```

### SSH Key Authentication

```json
{
  "SftpBlob": {
    "Host": "sftp.example.com",
    "Username": "user",
    "PrivateKeyPath": "/path/to/key",
    "PrivateKeyPassphrase": "optional-passphrase"
  }
}
```

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Hosting`
- `SSH.NET`

## Side Effects

- Registers `IBlobStorage` as singleton
- Opens SSH/SFTP connections to remote server
---
# Headless.Blobs.CloudflareR2

Cloudflare R2 implementation of `IBlobStorage`, running R2 as a private, S3-compatible blob backend on the reused AWS S3 engine.

## Problem Solved

R2 speaks the S3 API but cannot use the AWS provider as-is: the endpoint, path-style addressing, and AWS SDK v4 checksum defaults need R2-specific configuration, and R2 has no ACL concept. This package configures an R2-tuned `IAmazonS3` and reuses `AwsBlobStorage`, making R2 a drop-in, cost-saving S3 replacement.

## Key Features

- Full `IBlobStorage` implementation for Cloudflare R2 (reuses the AWS S3 engine)
- Presigned download/upload URLs via `IPresignedUrlBlobStorage`
- R2-correct client config: path-style addressing, `auto` region, SDK v4 checksum settings R2 accepts
- R2 bucket naming normalization (no dots)
- Jurisdiction-aware endpoints (default, EU, FedRAMP)

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

// Buckets and keys are passed per operation:
await storage.UploadAsync(["my-bucket"], "reports/q1.pdf", stream);
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

## Behavior Notes

- Buckets are not auto-created (`AutoCreateContainer` defaults to `false`): R2 object-scoped tokens cannot create buckets. Pre-create buckets out of band or use a bucket-create-capable token with `CreateContainerAsync`.
- No ACLs / public access: `CannedAcl` is `null`. Use presigned URLs for time-limited private access; public serving (custom domains / `r2.dev`) is out of scope.
- Single PUT is capped at ~5 GiB, the same as S3.

## Dependencies

- `Headless.Blobs.Aws`
- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `AWSSDK.S3`

## Side Effects

- Registers `IAmazonS3` (configured for R2) if not already registered
- Configures the shared `AwsBlobStorageOptions` with R2-safe defaults
- Registers `IBlobStorage` (the AWS S3 engine) as singleton
- Registers `IBlobNamingNormalizer` (R2 rules) as singleton
