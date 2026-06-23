---
domain: TUS (Resumable Uploads)
packages: Tus, Tus.Azure, Tus.DistributedLocks
---

# TUS (Resumable Uploads)

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [TUS Protocol Flow](#tus-protocol-flow)
    - [Azure Block Blob Mapping](#azure-block-blob-mapping)
    - [Concurrent PATCH Safety](#concurrent-patch-safety)
- [Headless.Tus](#headlesstus)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Tus.Azure](#headlesstusazure)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Tus.DistributedLocks](#headlesstusdistributedlocks)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)

> TUS protocol implementation for resumable file uploads with Azure Blob Storage backend and distributed lock support.

## Quick Orientation

Three packages compose the TUS domain:

| Package | Role |
|---|---|
| `Headless.Tus` | Base dependency: wires `tusdotnet` into ASP.NET Core; all TUS packages depend on this. |
| `Headless.Tus.Azure` | Storage backend: `TusAzureStore` stores upload chunks in Azure Blob Storage using block blobs. |
| `Headless.Tus.DistributedLocks` | Locking add-on: `DistributedLockTusLockProvider` prevents concurrent PATCH corruption across nodes. |

`Headless.Tus` alone is not usable for uploads — it has no store. Always pair it with `Headless.Tus.Azure` (or another future store). Add `Headless.Tus.DistributedLocks` for multi-instance deployments.

Minimal Azure setup:

```csharp
var store = new TusAzureStore(blobServiceClient, new TusAzureStoreOptions
{
    ContainerName = "uploads",
    BlobPrefix = "tus/",
    CreateContainerIfNotExists = true
});

app.MapTus("/files", async _ => new DefaultTusConfiguration
{
    Store = store,
    UrlPath = "/files"
});
```

## Agent Instructions

- `Headless.Tus` is a base dependency only — it has no store implementation. Always add `Headless.Tus.Azure` for upload storage.
- `TusAzureStore` must be constructed manually (`new TusAzureStore(blobServiceClient, options)`); it is not registered automatically in DI. Construct it where you build `DefaultTusConfiguration`.
- For multi-instance deployments, always add `Headless.Tus.DistributedLocks` and call `services.AddDistributedLockTusLockProvider()`. Without it, concurrent PATCH requests for the same file from different nodes can corrupt block lists.
- The distributed lock add-on requires `IDistributedLock` in DI. Register a Headless distributed lock provider first, for example: `services.AddHeadlessDistributedLocks(setup => setup.UseRedis())`.
- Set `FileLockProvider` in your `DefaultTusConfiguration` factory to the `ITusFileLockProvider` resolved from DI — it is not wired automatically. A single-node deployment can omit it and use tusdotnet's in-process lock.
- `BlobMaxChunkSize` must be ≤ 100 MB (Azure block blob limit), and `BlobDefaultChunkSize` must be 1 byte–100 MB and not exceed `BlobMaxChunkSize`. The store auto-selects chunk size from `BlobDefaultChunkSize` vs `BlobMaxChunkSize` based on declared upload length.
- An upload is capped at Azure's 50,000 committed blocks; a too-small chunk size on a very large upload throws a `TusStoreException` at commit time. Increase `BlobDefaultChunkSize`/`BlobMaxChunkSize` to use fewer, larger blocks.
- User `Upload-Metadata` keys are normalized to Azure's letter/digit/underscore charset. Keys that collide after normalization, or that collide with reserved `tus_*` keys, are rejected with a `TusStoreException`.
- Checksum verification (SHA1, SHA256, SHA512, MD5) is handled automatically when the TUS client sends `Upload-Checksum` headers. Blocks are staged but not committed until checksum passes; mismatched blocks are left uncommitted (Azure auto-removes them after 7 days).
- The concatenation extension uses server-side block copy (`StageBlockFromUri`) when available, falling back to streaming when it is not — Azurite returns HTTP 501, and a private container returns HTTP 403 for the bare source URI (no SAS).
- `CreateContainerIfNotExists = true` calls `CreateIfNotExists` synchronously in the `TusAzureStore` constructor — ensure the `BlobServiceClient` has container-create permissions before constructing the store.
- `ITusAzureBlobHttpHeadersProvider` lets you inject custom `Content-Type` / cache headers per file. Default sets `application/octet-stream`.

## Core Concepts

### TUS Protocol Flow

TUS is an HTTP-based protocol for resumable uploads. The key operations are:

1. **POST /files** — Upload creation. Client declares `Upload-Length` (or defers it). Server returns a `Location` URL with a file ID and `Upload-Offset: 0`.
2. **PATCH /files/{id}** — Chunk upload. Client sends bytes starting at `Upload-Offset`. Server appends and returns the new offset.
3. **HEAD /files/{id}** — Resume check. Client fetches current `Upload-Offset` to know where to resume after a disconnect.
4. **DELETE /files/{id}** (Termination extension) — Cancels and cleans up.

`tusdotnet` handles the HTTP negotiation; the store (here, `TusAzureStore`) handles where bytes land.

### Azure Block Blob Mapping

Azure Block Blobs decompose a file into independently-uploadable blocks that are committed in a single atomic operation. `TusAzureStore` maps TUS chunks onto Azure blocks:

- Each PATCH appends one or more staged blocks (split by `BlobDefaultChunkSize` / `BlobMaxChunkSize`).
- A block list commit makes the blocks durable and visible as blob content.
- For checksum uploads, blocks are staged but the commit is deferred until `VerifyChecksumAsync` passes — if checksum fails, staged blocks are left uncommitted and Azure discards them within 7 days.

This is why `GetUploadOffsetAsync` sums committed block sizes, not the blob's `ContentLength` property.

### Concurrent PATCH Safety

The TUS protocol allows only one concurrent PATCH for a given file, but enforcing this across multiple app instances requires explicit locking. Use **`DistributedLockTusLockProvider`** (from `Headless.Tus.DistributedLocks`): it wraps `IDistributedLock` with `AcquireTimeout = TimeSpan.Zero` and auto-extending leases. Non-blocking — tusdotnet receives `false` from `Lock()` and returns `409 Conflict` immediately, and a crashed holder's lease expires so the file is not stuck.

Register it with `services.AddDistributedLockTusLockProvider()` after registering an `IDistributedLock` backend (Redis, SQL Server, …). A single-node deployment can omit it and rely on tusdotnet's in-process lock.

> The store writes blocks to the upload blob without holding an Azure blob lease on it, so do **not** lock that blob directly — a lease on the upload blob would make the store's own writes fail with `412`. Coordinate PATCHes through `DistributedLockTusLockProvider` (a separate lock resource) instead.

---

## Headless.Tus

Base dependency that wires `tusdotnet` into the ASP.NET Core pipeline. Contains no store; exists to share the `tusdotnet` dependency and Headless hosting infrastructure across TUS packages.

### Problem Solved

Provides a consistent `tusdotnet` integration point for all TUS store packages so each provider does not independently manage endpoint wiring and version alignment.

### Key Features

- Shared `tusdotnet` dependency (all TUS packages reference this one)
- `Headless.Hosting` wiring for ASP.NET Core middleware

### Installation

```bash
dotnet add package Headless.Tus
```

### Quick Start

`Headless.Tus` is a base package. Add `Headless.Tus.Azure` for a complete upload setup. This package does not need to be installed directly; it is pulled in transitively.

### Configuration

None.

### Dependencies

- `tusdotnet`
- `Headless.Hosting`

### Side Effects

None.

---

## Headless.Tus.Azure

Azure Blob Storage TUS store implementation.

### Problem Solved

Provides `TusAzureStore`, a complete `ITusStore` implementation that backs resumable uploads with Azure Blob Storage block blobs. Supports all major TUS extensions: Creation, CreationDeferLength, Concatenation, Expiration, Checksum, and Termination.

### Key Features

- `TusAzureStore` — full `ITusStore` implementation backed by Azure block blobs; also implements `ITusPipelineStore` for zero-copy `PipeReader`-based ingestion
- TUS extensions supported:
  - **Creation** (`ITusCreationStore`) — `CreateFileAsync`, `GetUploadMetadataAsync`
  - **CreationDeferLength** (`ITusCreationDeferLengthStore`) — `SetUploadLengthAsync`
  - **Concatenation** (`ITusConcatenationStore`) — server-side block copy with streaming fallback for emulators
  - **Expiration** (`ITusExpirationStore`) — `SetExpirationAsync`, `GetExpiredFilesAsync`, `RemoveExpiredFilesAsync`
  - **Checksum** (`ITusChecksumStore`) — SHA1, SHA256, SHA512, MD5; constant-time comparison; deferred block commit
  - **Termination** (`ITusTerminationStore`) — `DeleteFileAsync`
  - **Readable** (`ITusReadableStore`) — `GetFileAsync` returns `ITusFile`
- `ITusAzureBlobHttpHeadersProvider` / `DefaultTusAzureBlobHttpHeadersProvider` — per-file HTTP header customization (Content-Type, cache control)
- Adaptive chunk sizing: automatic selection between `BlobDefaultChunkSize` (4 MB) and `BlobMaxChunkSize` (100 MB) based on declared upload size
- Pooled buffer stream splitting via `ArrayPool<byte>` to minimize allocations during PATCH ingestion

### Design Notes

**Block blob chunking and checksum deferral.** TUS Checksum verification happens _after_ all PATCH data is staged. `TusAzureStore` stages blocks during `AppendDataAsync` but defers the commit list until `VerifyChecksumAsync` succeeds. The staged block IDs and pre-calculated checksum are written to blob metadata (`tus_last_chunk_blocks`, `tus_last_chunk_checksum`). On success, blocks are committed atomically together with metadata update. On mismatch, blocks are left uncommitted and Azure auto-purges them within 7 days. This means `GetUploadOffsetAsync` reads committed block sizes — not the blob `ContentLength` property — to report accurate offset to resuming clients.

**Server-side concatenation.** Final-file creation uses `StageBlockFromUri` to copy block ranges across blobs without moving data through the application server. When server-side copy is unavailable — Azurite returns HTTP 501, and a private container returns HTTP 403 because the source blob is not readable via its bare URI (no SAS) — the store falls back to download-then-re-upload streaming automatically.

**Constructor-time container init.** When `CreateContainerIfNotExists = true`, `_containerClient.CreateIfNotExists(ContainerPublicAccessType)` is called synchronously in the constructor. If the `BlobServiceClient` lacks container-create permission, construction fails.

### Installation

```bash
dotnet add package Headless.Tus.Azure
```

### Quick Start

```csharp
using Azure.Storage.Blobs;
using Headless.Tus.Options;
using Headless.Tus.Services;
using tusdotnet.Models;

var builder = WebApplication.CreateBuilder(args);

var blobServiceClient = new BlobServiceClient(
    builder.Configuration["Azure:Storage:ConnectionString"]
);

var tusStore = new TusAzureStore(
    blobServiceClient,
    new TusAzureStoreOptions
    {
        ContainerName = "uploads",
        BlobPrefix = "tus/",
        CreateContainerIfNotExists = true
    }
);

var app = builder.Build();

app.MapTus("/files", async _ => new DefaultTusConfiguration
{
    Store = tusStore,
    UrlPath = "/files"
});

app.Run();
```

Serializing concurrent PATCHes across nodes (multi-node deployments) with `Headless.Tus.DistributedLocks`:

```csharp
using Headless.Tus;            // AddDistributedLockTusLockProvider
using tusdotnet.Interfaces;

// Register an IDistributedLock backend (Redis, SQL Server, …) first, then the TUS lock adapter:
builder.Services.AddDistributedLockTusLockProvider();

app.MapTus("/files", httpContext => Task.FromResult(new DefaultTusConfiguration
{
    Store = tusStore,
    UrlPath = "/files",
    FileLockProvider = httpContext.RequestServices.GetRequiredService<ITusFileLockProvider>()
}));
```

A single-node deployment can omit the lock provider and rely on tusdotnet's in-process lock.

With custom HTTP headers per upload:

```csharp
using Azure.Storage.Blobs.Models;
using Headless.Tus.Services;

public sealed class MyHeadersProvider : ITusAzureBlobHttpHeadersProvider
{
    public ValueTask<BlobHttpHeaders> GetBlobHttpHeadersAsync(Dictionary<string, string> metadata)
    {
        metadata.TryGetValue("filetype", out var contentType);

        return ValueTask.FromResult(new BlobHttpHeaders
        {
            ContentType = contentType ?? "application/octet-stream"
        });
    }
}

var store = new TusAzureStore(
    blobServiceClient,
    options,
    blobHttpHeadersProvider: new MyHeadersProvider()
);
```

### Configuration

`TusAzureStoreOptions` — all properties have defaults:

| Option | Default | Notes |
|---|---|---|
| `ContainerName` | `"tus-uploads"` | Azure Blob container name. |
| `BlobPrefix` | `"uploads/"` | Prefix applied to all blob names in the container. |
| `CreateContainerIfNotExists` | `true` | Calls `CreateIfNotExists` in the constructor. |
| `ContainerPublicAccessType` | `PublicAccessType.None` | Access type used when creating the container. |
| `EnableChunkSplitting` | `true` | Splits large PATCH bodies into multiple Azure blocks. |
| `BlobDefaultChunkSize` | `4 MB` | Default block size for medium uploads. Must be 1 byte–100 MB and not exceed `BlobMaxChunkSize`. |
| `BlobMaxChunkSize` | `100 MB` | Block size used for uploads ≥ 100 MB. |

Chunk size selection logic: uploads < 10 MB use `min(BlobDefaultChunkSize, fileSize)`; uploads 10–100 MB use `BlobDefaultChunkSize`; uploads ≥ 100 MB use `BlobMaxChunkSize`.

### Dependencies

- `Headless.Tus`
- `Azure.Storage.Blobs`
- `Azure.Storage.Blobs.Batch`
- `Microsoft.Extensions.Azure`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

- Synchronously calls `BlobContainerClient.CreateIfNotExists` during `TusAzureStore` construction when `CreateContainerIfNotExists = true`.
- No DI registrations — `TusAzureStore` is constructed manually. For cross-node PATCH locking, register `Headless.Tus.DistributedLocks`.

---

## Headless.Tus.DistributedLocks

Distributed lock-based TUS file lock provider, using `Headless.DistributedLocks` to prevent concurrent PATCH corruption across multiple application instances.

### Problem Solved

The TUS protocol allows only one concurrent PATCH per file. On single-instance deployments, `tusdotnet`'s default in-process locking suffices. On multi-instance deployments (load-balanced or Kubernetes pods), each instance has its own in-process lock table, so two nodes can simultaneously PATCH the same file, producing interleaved blocks and corrupted uploads. `DistributedLockTusLockProvider` uses the framework's `IDistributedLock` to coordinate across nodes.

### Key Features

- `DistributedLockTusLockProvider` — `ITusFileLockProvider` backed by `IDistributedLock`
- `DistributedLockTusFileLock` — `ITusFileLock` that calls `TryAcquireAsync` with zero wait; returns `false` immediately if another node holds the lock (tusdotnet returns `409 Conflict` to the client)
- Lock resource key format: `tus-file-lock-{fileId}`
- Compatible with any `IDistributedLock` backend (Redis, in-memory, etc.)
- Single `AddDistributedLockTusLockProvider()` extension on `IServiceCollection`

### Installation

```bash
dotnet add package Headless.Tus.DistributedLocks
```

### Quick Start

```csharp
using Headless.Tus;
using tusdotnet.Interfaces;
using tusdotnet.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Register a Headless distributed lock backend (Redis shown; any backend works)
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

// 2. Register the TUS lock provider
builder.Services.AddDistributedLockTusLockProvider();

var app = builder.Build();

// 3. Wire the lock provider into the TUS configuration
app.MapTus("/files", async ctx =>
{
    var lockProvider = ctx.RequestServices.GetRequiredService<ITusFileLockProvider>();

    return new DefaultTusConfiguration
    {
        Store = tusStore,       // your TusAzureStore instance
        UrlPath = "/files",
        FileLockProvider = lockProvider
    };
});

app.Run();
```

### Configuration

None beyond registering an `IDistributedLock` provider. The lock acquires with `AcquireTimeout = TimeSpan.Zero` (non-blocking) and `TimeUntilExpires = Timeout.InfiniteTimeSpan` (no expiry while held).

### Dependencies

- `Headless.Tus`
- `Headless.DistributedLocks.Abstractions`

### Side Effects

- `AddDistributedLockTusLockProvider()` registers `ITusFileLockProvider` as a singleton (`DistributedLockTusLockProvider`).
- Requires `IDistributedLock` to be registered in DI before this call.
