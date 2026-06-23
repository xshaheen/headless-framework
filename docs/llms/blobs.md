---
domain: Blob Storage
packages: Blobs.Abstractions, Blobs.Core, Blobs.Aws, Blobs.Azure, Blobs.CloudflareR2, Blobs.FileSystem, Blobs.Redis, Blobs.SshNet
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
- [Headless.Blobs.Core](#headlessblobscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Blobs.Aws](#headlessblobsaws)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
        - [appsettings.json](#appsettingsjson)
        - [Options](#options)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Blobs.Azure](#headlessblobsazure)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
        - [appsettings.json](#appsettingsjson-1)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Blobs.CloudflareR2](#headlessblobscloudflarer2)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
        - [appsettings.json](#appsettingsjson-2)
    - [Design Notes](#design-notes-1)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.Blobs.FileSystem](#headlessblobsfilesystem)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
        - [appsettings.json](#appsettingsjson-3)
        - [Options](#options-1)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)
- [Headless.Blobs.Redis](#headlessblobsredis)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-6)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-6)
- [Headless.Blobs.SshNet](#headlessblobssshnet)
    - [Problem Solved](#problem-solved-7)
    - [Key Features](#key-features-7)
    - [Installation](#installation-7)
    - [Quick Start](#quick-start-7)
    - [Configuration](#configuration-7)
        - [appsettings.json](#appsettingsjson-4)
        - [SSH Key Authentication](#ssh-key-authentication)
    - [Dependencies](#dependencies-7)
    - [Side Effects](#side-effects-7)

> Provider-agnostic file/blob storage with implementations for AWS S3, Azure Blob, local filesystem, Redis, and SFTP.

## Quick Orientation

Install `Headless.Blobs.Abstractions`, `Headless.Blobs.Core`, and one or more provider packages. Register every store through a single `AddHeadlessBlobs(...)` call: pick a default with `Use{Provider}(...)` and add named stores with `AddNamed(name, i => i.Use{Provider}(...))`. Code against `IBlobStorage` — never reference concrete provider types in application code.

Provider selection guide:
- **Development/testing**: `Headless.Blobs.FileSystem` — no external dependencies, stores files on disk.
- **Production (AWS)**: `Headless.Blobs.Aws` — full S3 integration with bulk operations and presigned URLs.
- **Production (Azure)**: `Headless.Blobs.Azure` — Azure Blob Storage with Batch API and Azure.Identity support.
- **Production (Cloudflare R2)**: `Headless.Blobs.CloudflareR2` — private, S3-compatible storage on the reused AWS engine; a cost-saving S3 replacement.
- **SFTP/legacy**: `Headless.Blobs.SshNet` — SFTP protocol for remote servers and legacy system integration.
- **Small cached blobs**: `Headless.Blobs.Redis` — Redis-backed storage for small, ephemeral blobs only (default 10 MB limit).

The default store registers as a plain (unkeyed) `IBlobStorage` singleton; named stores register as keyed `IBlobStorage` singletons and resolve through `IBlobStorageProvider`. Container paths are arrays of strings (e.g., `["uploads", "images"]`).

## Agent Instructions

- Always depend on `IBlobStorage` from `Headless.Blobs.Abstractions` — never reference `AwsBlobStorage`, `AzureBlobStorage`, or other concrete types in service code.
- Register all stores through `AddHeadlessBlobs(...)` from `Headless.Blobs.Core`. Choose a default with `Use{Provider}(...)` and add named stores with `AddNamed(name, i => i.Use{Provider}(...))`. Use `UseFileSystem` for local development and testing; `UseAws`, `UseAzure`, or `UseCloudflareR2` for production.
- Presigned URLs are a per-store, opt-in capability. For **named** stores: AWS, Azure, and CloudflareR2 register a keyed `IPresignedUrlBlobStorage` — resolve via `[FromKeyedServices("name")] IPresignedUrlBlobStorage` or `sp.GetRequiredKeyedService<IPresignedUrlBlobStorage>("name")`. For the **default** store: feature-detect by casting (`storage is IPresignedUrlBlobStorage presigned`). FileSystem, Redis, and SshNet are never presigned-capable.
- There is no global (unkeyed) `IPresignedUrlBlobStorage` registration. Do not attempt to inject it without a key.
- Container auto-create on upload/copy is opt-in and cached once per container per instance. The S3 engine (AWS and R2) uses `AwsBlobStorageOptions.AutoCreateContainer` (AWS default `true`, R2 default `false`); Azure uses `AzureStorageOptions.AutoCreateContainer` (default `true`). With it off, pre-create containers or call `CreateContainerAsync`. The S3 engine also no longer pre-checks the bucket on read/exists/delete.
- Redis blob storage (`Blobs.Redis`) is for small blobs only (metadata, thumbnails, temporary uploads). The default `MaxBlobSizeBytes` is 10 MB. For large files, use S3 or Azure. The `UseRedis(IConfiguration)` overload cannot bind the required `ConnectionMultiplexer` interface property — use the `Action<RedisBlobStorageOptions>` overload to set it.
- Always dispose the result of `OpenReadStreamAsync()` promptly — holding it may exhaust connection pools. Use `await using`.
- Container paths are string arrays, not slash-delimited strings: `["uploads", "images"]` not `"uploads/images"`.
- Naming is normalized two-tier through each provider's `IBlobNamingNormalizer`: the **first** container segment is the backend bucket/container (strict backend rules — e.g. lowercase, length, allowed characters) and the **remaining** segments plus the blob name are the object path (lenient — validated, not rewritten). This is applied uniformly by every provider; do not manually normalize paths.
- Metadata is a `Dictionary<string, string?>`. For `FileSystem` provider, metadata is stored as companion JSON files. For `Redis`, metadata is stored alongside blobs in Redis.
- `SshNet` supports both password and SSH key authentication. Use `PrivateKey` for key-based auth.
- A default store is optional and there is at most one (injected as plain `IBlobStorage`); a named-only configuration is valid and leaves plain `IBlobStorage` unregistered. The same provider may back multiple named stores with isolated config. Resolve named stores with `IBlobStorageProvider.GetStorage("name")` or `[FromKeyedServices("name")] IBlobStorage`. Calling `AddHeadlessBlobs` more than once on the same service collection throws.

## Choosing a Provider

Pick one provider per store (default or named) based on where the bytes must live and which capabilities you need.

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Blobs.FileSystem` | Local dev, testing, or single-node on-prem with no cloud dependency | Multi-node or horizontally-scaled deployments (no shared storage) | Not distributed; metadata kept as companion JSON files |
| `Headless.Blobs.Aws` | Production on AWS; need presigned URLs and bulk operations | Not on AWS, or egress cost is a concern | Ties you to S3 pricing and the AWS SDK |
| `Headless.Blobs.CloudflareR2` | S3-compatible storage with low egress cost and private buckets | You need public serving via ACLs, or bucket auto-create from the app | No ACL concept; buckets must be pre-created (no auto-create) |
| `Headless.Blobs.Azure` | Production on Azure; want Entra ID auth and SAS presigned URLs | Not on Azure | Requires a `BlobServiceClient`; extra SAS rules for AAD clients |
| `Headless.Blobs.SshNet` | Files must land on a remote SFTP/SSH server or legacy system | High-throughput or presigned-URL workloads | Slower; no presigned URLs; opens live SSH connections |
| `Headless.Blobs.Redis` | Small, ephemeral blobs (thumbnails, temp uploads) needing fast access | Large files (default 10 MB cap) or durable storage | In-memory cost; not for large or long-lived blobs |

---

## Headless.Blobs.Abstractions

Defines the unified interfaces for blob/file storage operations across all providers.

### Problem Solved

Application code needs a single, provider-agnostic API for file storage so it can switch between cloud providers, local storage, or test fakes without change. This package defines `IBlobStorage` and the supporting contracts; it carries no implementation and no DI registrations.

### Key Features

- `IBlobStorage` — core interface covering upload, download (`OpenReadStreamAsync`), copy, rename, delete, exists, info, paged listing, bulk upload/delete, and container management.
- `IPresignedUrlBlobStorage` — optional presigned GET + PUT URL capability; implemented only by AWS, Azure, and CloudflareR2.
- `IBlobStorageProvider` — resolves named `IBlobStorage` instances registered through the setup builder (`GetStorage(name)`, `GetStorageOrNull(name)`, `RegisteredNames`).
- `IBlobNamingNormalizer` — provider-specific two-tier path normalization contract.
- Metadata support via `Dictionary<string, string?>`.

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
        // Dispose result promptly — holding it may exhaust connection pools.
        await using var result = await storage.OpenReadStreamAsync(["uploads", "images"], fileName, ct);
        if (result is null) return null;

        using var reader = new StreamReader(result.Stream);
        return await reader.ReadToEndAsync(ct);
    }
}
```

Resolve a named store or check for presigned support:

```csharp
public sealed class StorageService(
    IBlobStorageProvider provider,
    [FromKeyedServices("archive")] IBlobStorage archiveStorage)
{
    // Validate an externally-supplied name before resolving:
    public bool IsKnownStore(string name) => provider.RegisteredNames.Contains(name);

    // Resolve by name:
    public IBlobStorage GetByName(string name) => provider.GetStorage(name); // throws if not found
    public IBlobStorage? TryGetByName(string name) => provider.GetStorageOrNull(name); // null if not found

    // Feature-detect presigned on the default store:
    public async Task<Uri?> TryGetPresignedUrlAsync(IBlobStorage storage, string[] container, string blob)
    {
        if (storage is not IPresignedUrlBlobStorage presigned)
            return null;
        return await presigned.GetPresignedDownloadUrlAsync(container, blob, TimeSpan.FromMinutes(15));
    }
}
```

### Configuration

None. This is an abstractions-only package.

### Dependencies

- `Headless.Extensions`
- `Headless.Serializer.Json`

### Side Effects

None. This is an abstractions package.

---

## Headless.Blobs.Core

Unified setup builder for composing one or more named blob stores in a single DI container.

### Problem Solved

A single application often needs several blob stores at once — images on one backend, documents on another, scratch files on a third — and sometimes two instances of the same provider (a production and a staging bucket). Registering providers directly only yields one `IBlobStorage`; a second registration silently shadows the first. This package adds `AddHeadlessBlobs(...)`, a single entry point that composes an optional default plus any number of independently-configured named stores, each resolvable by name.

### Key Features

- `AddHeadlessBlobs(Action<HeadlessBlobsSetupBuilder>)` — single registration entry point for all blob stores.
- Optional default store (at most one), injectable as plain `IBlobStorage`.
- Unlimited named stores with unique names; the same provider may back several.
- `IBlobStorageProvider` — resolves named stores by name; exposes `RegisteredNames` for safe pre-validation.
- Keyed `IBlobStorage` resolution via `[FromKeyedServices("name")]` or `GetRequiredKeyedService<IBlobStorage>("name")`.
- Deferred, gate-validated registration: a misconfigured setup (duplicate default, duplicate name, zero providers for a named store) throws before mutating the service collection.

### Design Notes

- Each provider package contributes `Use{Provider}` extension members on `HeadlessBlobsSetupBuilder` (default) and `HeadlessBlobInstanceBuilder` (named). Named stores register as keyed `IBlobStorage` services, never touching the default (unkeyed) registration, so a named-only configuration leaves plain `IBlobStorage` unregistered.
- Each store is fully isolated: its own named options, its own provider client, and its own `IBlobNamingNormalizer`. Ambient services (`IMimeTypeProvider`, `IClock`) are shared across stores.
- Presigned support is a per-store capability. For named stores, AWS, Azure, and CloudflareR2 also register a keyed `IPresignedUrlBlobStorage` forward for direct injection. For the default store, feature-detect by casting (`storage is IPresignedUrlBlobStorage`).
- `IBlobStorageProvider.RegisteredNames` contains only **named** instance names; the default/unnamed store is excluded. Use it to validate an externally-supplied name before calling `GetStorage` rather than probe-and-catch.

### Installation

```bash
dotnet add package Headless.Blobs.Core
```

Add at least one provider package (`Headless.Blobs.Aws`, `Headless.Blobs.Azure`, `Headless.Blobs.CloudflareR2`, `Headless.Blobs.FileSystem`, `Headless.Blobs.Redis`, or `Headless.Blobs.SshNet`).

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
{
    // Default store — injected as plain IBlobStorage.
    blobs.UseCloudflareR2(options =>
    {
        options.AccountId = builder.Configuration["R2:AccountId"]!;
        options.AccessKeyId = builder.Configuration["R2:AccessKeyId"]!;
        options.SecretAccessKey = builder.Configuration["R2:SecretAccessKey"]!;
    });

    // Named store — injected as keyed IBlobStorage("docs").
    blobs.AddNamed("docs", instance => instance.UseAzure(
        setupAction: options => { },
        clientFactory: _ => new BlobServiceClient(builder.Configuration["Azure:Docs:ConnectionString"])));

    // Two named instances of the same provider, each with independent config.
    blobs.AddNamed("scratch", instance => instance.UseFileSystem(
        options => options.BaseDirectoryPath = "/tmp/blobs"));

    blobs.AddNamed("archive", instance => instance.UseAws(
        options => { },
        awsOptions: builder.Configuration.GetAWSOptions("AWS:Archive")));
});
```

Resolve stores:

```csharp
// Default store — plain injection.
public sealed class UploadService(IBlobStorage storage) { }

// Named store — keyed injection.
public sealed class DocsService([FromKeyedServices("docs")] IBlobStorage docsStorage) { }

// Named store — via IBlobStorageProvider.
public sealed class MultiStoreService(IBlobStorageProvider provider)
{
    public IBlobStorage GetDocs() => provider.GetStorage("docs");
    public bool HasStore(string name) => provider.RegisteredNames.Contains(name);
}

// Named presigned URL (AWS/Azure/R2 only).
public sealed class PresignedService(
    [FromKeyedServices("docs")] IPresignedUrlBlobStorage presigned)
{
    public Task<Uri> GetDownloadUrl(string[] container, string blob) =>
        presigned.GetPresignedDownloadUrlAsync(container, blob, TimeSpan.FromHours(1));
}
```

### Configuration

No options of its own. Each store's options are configured through its provider's `Use{Provider}` overloads (`Action<TOptions>`, `IConfiguration`, or `Action<TOptions, IServiceProvider>`).

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Extensions`

### Side Effects

- Registers `IBlobStorageProvider` as singleton (backed by the container's keyed `IBlobStorage` registrations).
- Registers a called-once marker that rejects a second `AddHeadlessBlobs` call on the same service collection.
- Default `Use{Provider}`: registers `IBlobStorage` as unkeyed singleton.
- `AddNamed(... Use{Provider})`: registers `IBlobStorage` as keyed singleton (`name`). For AWS, Azure, and CloudflareR2 also registers `IPresignedUrlBlobStorage` as keyed singleton (`name`). For SshNet, also registers a keyed `SftpClientPool` singleton (`name`).
- There is no global (unkeyed) `IPresignedUrlBlobStorage` registration.

---

## Headless.Blobs.Aws

AWS S3 implementation of `IBlobStorage` for storing files in Amazon S3.

### Problem Solved

Provides integration with AWS S3 for blob storage using the unified `IBlobStorage` abstraction, with per-store S3 client construction, presigned URL support, and opt-in bucket auto-create.

### Key Features

- Full `IBlobStorage` implementation for AWS S3.
- Bulk upload/delete with optimized batching.
- Two-tier name normalization: bucket name normalized to S3 rules; object-key path segments validated and preserved.
- Metadata support.
- Presigned download/upload URLs via `IPresignedUrlBlobStorage` (named stores only; feature-detect via cast for the default store).
- Opt-in, cached bucket auto-create (`AutoCreateContainer`, default `true`).
- Per-store `IAmazonS3` constructed via `S3ClientFactory`; optional `AWSOptions` to override the SDK credential/region chain.

### Installation

```bash
dotnet add package Headless.Blobs.Aws
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Default store — AWS SDK credential/region chain applies unless overridden.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseAws(options => { }, awsOptions: builder.Configuration.GetAWSOptions()));

// Explicit credentials:
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseAws(
        options => { },
        new AWSOptions
        {
            Region = RegionEndpoint.USEast1,
            Credentials = new BasicAWSCredentials("access-key", "secret-key"),
        }));

// Named store with per-store credentials; keyed IPresignedUrlBlobStorage registered automatically.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.AddNamed("archive", instance => instance.UseAws(
        options => { },
        awsOptions: builder.Configuration.GetAWSOptions("AWS:Archive"))));
```

Buckets and keys are passed per operation:

```csharp
await storage.UploadAsync(["my-bucket"], "reports/q1.pdf", stream);

// Presigned URL on a named store:
// [FromKeyedServices("archive")] IPresignedUrlBlobStorage presigned
var url = await presigned.GetPresignedDownloadUrlAsync(["my-bucket"], "reports/q1.pdf", TimeSpan.FromHours(1));

// Presigned URL on the default store — feature-detect:
if (storage is IPresignedUrlBlobStorage presigned)
    var url = await presigned.GetPresignedDownloadUrlAsync(["my-bucket"], "file.pdf", TimeSpan.FromHours(1));
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
options.AutoCreateContainer = true;            // create buckets on upload/copy (default true; set false for R2)
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

Registered via `AddHeadlessBlobs(b => b.UseAws(...))` or `AddNamed("name", i => i.UseAws(...))`:

- Default (`UseAws`): registers `IBlobStorage` as unkeyed singleton. The per-store `IAmazonS3` is constructed inline; it is not registered in the DI container.
- Named (`AddNamed ... UseAws`): registers `IBlobStorage` as keyed singleton (`name`); registers `IPresignedUrlBlobStorage` as keyed singleton (`name`, forwarded from the keyed `IBlobStorage`). The per-store `IAmazonS3` is constructed inline.

---

## Headless.Blobs.Azure

Azure Blob Storage implementation of `IBlobStorage` for storing files in Azure.

### Problem Solved

Provides integration with Azure Blob Storage using the unified `IBlobStorage` abstraction, with `BlobServiceClient` resolution from DI or a per-store factory, presigned SAS URL support, and opt-in container auto-create.

### Key Features

- Full `IBlobStorage` implementation for Azure Blob Storage.
- Bulk operations with Azure Batch API.
- Opt-in, cached container auto-create (`AutoCreateContainer`, default `true`).
- Metadata support.
- Presigned download/upload URLs via `IPresignedUrlBlobStorage` (SAS-based; named stores only — feature-detect via cast for the default store).
- Per-store `BlobServiceClient` from an optional `clientFactory`; falls back to the ambient `BlobServiceClient` from DI.

### Installation

```bash
dotnet add package Headless.Blobs.Azure
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register a BlobServiceClient in DI (used when no clientFactory is supplied).
builder.Services.AddSingleton(new BlobServiceClient(builder.Configuration["Azure:Storage:ConnectionString"]));

builder.Services.AddHeadlessBlobs(blobs =>
{
    // Default store — consumes the ambient BlobServiceClient from DI.
    blobs.UseAzure(options => { });

    // Named store on a different account — per-store clientFactory overrides the DI client.
    // Also registers keyed IPresignedUrlBlobStorage("archive") automatically.
    blobs.AddNamed("archive", instance => instance.UseAzure(
        setupAction: options => { },
        clientFactory: _ => new BlobServiceClient("<archive-connection-string>")));
});
```

When no `clientFactory` is supplied, the `BlobServiceClient` must be registered in DI before first use. Absence is detected at resolution time, not at startup.

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

Registered via `AddHeadlessBlobs(b => b.UseAzure(...))` or `AddNamed("name", i => i.UseAzure(...))`:

- Default (`UseAzure`): registers `IBlobStorage` as unkeyed singleton. Consumes `BlobServiceClient` from DI (or `clientFactory`) at resolution time.
- Named (`AddNamed ... UseAzure`): registers `IBlobStorage` as keyed singleton (`name`); registers `IPresignedUrlBlobStorage` as keyed singleton (`name`, forwarded from the keyed `IBlobStorage`).
- Presigned URLs require a `BlobServiceClient` that can sign: account-key clients sign locally; AAD/`DefaultAzureCredential` clients fall back to user-delegation SAS (requires `Storage Blob Delegator` role, capped at 7 days). A bare SAS-token or anonymous client throws `InvalidOperationException` at call time.

---

## Headless.Blobs.CloudflareR2

Cloudflare R2 implementation of `IBlobStorage`, running R2 as a private, S3-compatible blob backend on the reused AWS S3 engine.

### Problem Solved

R2 speaks the S3 API but cannot use the AWS provider as-is: the endpoint, path-style addressing, and AWS SDK v4 checksum defaults need R2-specific configuration, and R2 has no ACL concept. This package configures an R2-tuned `IAmazonS3` via `R2ClientFactory` and reuses `AwsBlobStorage`, making R2 a drop-in, cost-saving S3 replacement.

### Key Features

- Full `IBlobStorage` implementation for Cloudflare R2 (reuses the AWS S3 engine).
- Presigned download/upload URLs via `IPresignedUrlBlobStorage` (named stores only — feature-detect via cast for the default store).
- R2-correct client config: path-style addressing, `auto` region, SDK v4 checksum settings R2 accepts.
- R2 bucket naming normalization (no dots).
- Jurisdiction-aware endpoints (default, EU, FedRAMP).
- R2-safe defaults applied per named instance (`AwsBlobStorageOptions`): `CannedAcl = null`, `UseChunkEncoding = false`, `DisablePayloadSigning = true`, `AutoCreateContainer = false`.

### Installation

```bash
dotnet add package Headless.Blobs.CloudflareR2
```

### Quick Start

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

Bind with `blobs.UseCloudflareR2(builder.Configuration.GetSection("R2"))`.

### Design Notes

- **Buckets are not auto-created.** R2 object-scoped tokens cannot create buckets, so `AutoCreateContainer` defaults to `false`. Pre-create buckets out of band or use a bucket-create-capable token with `CreateContainerAsync`.
- **No ACLs / public access.** `CannedAcl` is `null`. Use presigned URLs for time-limited private access; public serving (custom domains / `r2.dev`) is out of scope.
- **Single PUT is capped at ~5 GiB**, the same as S3.

### Dependencies

- `Headless.Blobs.Aws` (the reused S3 engine)
- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `AWSSDK.S3`

### Side Effects

Registered via `AddHeadlessBlobs(b => b.UseCloudflareR2(...))` or `AddNamed("name", i => i.UseCloudflareR2(...))`:

- Default (`UseCloudflareR2`): registers `IBlobStorage` as unkeyed singleton. The per-store `IAmazonS3` (R2-tuned) is constructed inline; it is not registered in the DI container.
- Named (`AddNamed ... UseCloudflareR2`): configures named `AwsBlobStorageOptions` with R2 forced defaults; registers `IBlobStorage` as keyed singleton (`name`); registers `IPresignedUrlBlobStorage` as keyed singleton (`name`, forwarded from the keyed `IBlobStorage`). The per-store `IAmazonS3` is constructed inline.

---

## Headless.Blobs.FileSystem

Local file system implementation of `IBlobStorage` for development and on-premises scenarios.

### Problem Solved

Provides local file system storage using the unified `IBlobStorage` abstraction, for development, testing, and on-premises deployments without cloud dependencies.

### Key Features

- Full `IBlobStorage` implementation using local file system.
- Container mapping to directories.
- Metadata stored as companion JSON files.
- No external service dependencies.
- Cross-platform path handling.

### Installation

```bash
dotnet add package Headless.Blobs.FileSystem
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseFileSystem(options =>
        options.BaseDirectoryPath = Path.Combine(builder.Environment.ContentRootPath, "storage")));
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
options.BaseDirectoryPath = "/path/to/storage";  // required; the root directory for all containers
```

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Hosting`

### Side Effects

Registered via `AddHeadlessBlobs(b => b.UseFileSystem(...))` or `AddNamed("name", i => i.UseFileSystem(...))`:

- Default (`UseFileSystem`): registers `IBlobStorage` as unkeyed singleton.
- Named (`AddNamed ... UseFileSystem`): registers `IBlobStorage` as keyed singleton (`name`).
- No presigned URL support — `IPresignedUrlBlobStorage` is never registered for FileSystem stores.

---

## Headless.Blobs.Redis

Redis implementation of `IBlobStorage` for storing small, ephemeral blobs in Redis.

### Problem Solved

Provides high-speed blob storage for small files using Redis, for temporary files, cache data, or session-related binary content. Not a general-purpose store — the 10 MB default limit and Redis memory model make it unsuitable for large files.

### Key Features

- Full `IBlobStorage` implementation using Redis.
- Automatic key expiration support.
- Metadata stored alongside blobs in Redis.
- Fast read/write performance.

### Installation

```bash
dotnet add package Headless.Blobs.Redis
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// The IConnectionMultiplexer must be set in code — it cannot be bound from appsettings.json.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseRedis(options =>
        options.ConnectionMultiplexer = ConnectionMultiplexer.Connect("localhost:6379")));
```

### Configuration

`RedisBlobStorageOptions` requires an `IConnectionMultiplexer` instance. The `UseRedis(IConfiguration)` overload cannot bind this interface property — options validation fails at startup if `ConnectionMultiplexer` is not set via the `Action<RedisBlobStorageOptions>` overload.

| Option | Default | Description |
|--------|---------|-------------|
| `ConnectionMultiplexer` | *(required)* | `IConnectionMultiplexer` instance for Redis. |
| `MaxBlobSizeBytes` | 10 MB | Maximum blob size in bytes. Set to `0` to disable the limit. |
| `MaxBulkParallelism` | 10 | Maximum parallelism for bulk operations. |

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `StackExchange.Redis`

### Side Effects

Registered via `AddHeadlessBlobs(b => b.UseRedis(...))` or `AddNamed("name", i => i.UseRedis(...))`:

- Default (`UseRedis`): registers `IBlobStorage` as unkeyed singleton; registers `TimeProvider`, `IJsonOptionsProvider`, and `IJsonSerializer` as singletons (each via `TryAdd`, so existing registrations are kept).
- Named (`AddNamed ... UseRedis`): registers `IBlobStorage` as keyed singleton (`name`); same `TryAdd` registrations for shared services.
- No presigned URL support — `IPresignedUrlBlobStorage` is never registered for Redis stores.

---

## Headless.Blobs.SshNet

SFTP/SSH implementation of `IBlobStorage` for storing files on remote servers via SFTP.

### Problem Solved

Provides blob storage via SFTP/SSH for scenarios requiring file transfer to remote servers, legacy system integration, or secure file exchange with systems that do not expose a cloud API.

### Key Features

- Full `IBlobStorage` implementation using SFTP.
- SSH key and password authentication.
- Remote directory management.
- Metadata support via companion files.
- Connection pooling (`SftpClientPool`); each store owns its own pool.

### Installation

```bash
dotnet add package Headless.Blobs.SshNet
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseSsh(options =>
        options.ConnectionString = "sftp://user:password@sftp.example.com:22/home/user/uploads"));
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
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseSsh(options =>
    {
        options.ConnectionString = "sftp://user@sftp.example.com:22/home/user/uploads";
        options.PrivateKey = File.OpenRead("/path/to/key");
        options.PrivateKeyPassPhrase = "optional-passphrase"; // nullable
    }));
```

### Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Hosting`
- `SSH.NET`

### Side Effects

Registered via `AddHeadlessBlobs(b => b.UseSsh(...))` or `AddNamed("name", i => i.UseSsh(...))`:

- Default (`UseSsh`): registers `SftpClientPool` as unkeyed singleton; registers `IBlobStorage` as unkeyed singleton.
- Named (`AddNamed ... UseSsh`): registers `SftpClientPool` as keyed singleton (`name`); registers `IBlobStorage` as keyed singleton (`name`). Each named store owns its own pool instance bound to its named options.
- No presigned URL support — `IPresignedUrlBlobStorage` is never registered for SshNet stores.
