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
| `Headless.Tus` | Base package: protocol-level pieces every deployment needs — `TusCorsDefaults`/`WithTusHeaders()` for browser clients, `AddTusExpiredUploadsCleanup()` for expired-upload removal — plus the shared `tusdotnet` + `Headless.Hosting` references. |
| `Headless.Tus.Azure` | Storage backend: `TusAzureStore` stores upload chunks in Azure Blob Storage using block blobs. |
| `Headless.Tus.DistributedLocks` | Locking add-on: `DistributedLockTusLockProvider` prevents concurrent PATCH corruption across nodes. |

`Headless.Tus` has no store — pair it with `Headless.Tus.Azure` (or another future store). Add `Headless.Tus.DistributedLocks` for multi-instance deployments.

A complete resumable-upload backend (Azurite for local development: `docker run -d -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0`):

```csharp
using Headless.Tus;
using Headless.Tus.Services;
using Microsoft.Extensions.Azure;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Models.Expiration;

var builder = WebApplication.CreateBuilder(args);

// 1. The storage account ("UseDevelopmentStorage=true" targets Azurite).
builder.Services.AddAzureClients(clients =>
    clients.AddBlobServiceClient(builder.Configuration.GetConnectionString("AzureStorage")));

// 2. The store: options from the "Tus" config section (ContainerName, BlobPrefix, ...),
//    validated at startup.
builder.Services.AddTusAzureStore(builder.Configuration.GetSection("Tus"));

// 3. Reap expired incomplete uploads (uses the ITusExpirationStore registered above).
builder.Services.AddTusExpiredUploadsCleanup();

// 4. CORS for browser clients on another origin (tus-js-client, Uppy): expose the tus
//    response headers or every cross-origin upload fails on the first request.
builder.Services.AddCors(options =>
    options.AddPolicy("tus", policy => policy.WithOrigins("https://app.example.com").WithTusHeaders()));

var app = builder.Build();
app.UseCors("tus");

// 5. The tus endpoint.
app.MapTus(
    "/files",
    httpContext => Task.FromResult(new DefaultTusConfiguration
    {
        Store = httpContext.RequestServices.GetRequiredService<TusAzureStore>(),
        UrlPath = "/files",
        AllowedExtensions = TusExtensions.All,
        Expiration = new SlidingExpiration(TimeSpan.FromMinutes(30)),
    }));

app.Run();
```

Any tus 1.0.0 client works against this endpoint (`tus-js-client`, Uppy's `@uppy/tus`, tuspy, …). A complete runnable example — this backend plus a React frontend driving the endpoint with both Uppy Dashboard and `use-tus` — lives in [demo/Headless.Tus.Demo](../../demo/Headless.Tus.Demo/README.md).

## Agent Instructions

- `Headless.Tus` has no store implementation — always add `Headless.Tus.Azure` for upload storage. It does ship the cross-provider pieces: `TusCorsDefaults` / `WithTusHeaders()` and `AddTusExpiredUploadsCleanup()`.
- Browser clients on another origin need the tus CORS surface: use `policy.WithTusHeaders()` (allowed request headers + exposed response headers + methods incl. PATCH/DELETE). Without the exposed headers, `tus-js-client`/Uppy cannot read `Location`/`Upload-Offset` and uploads fail on the first request.
- Nothing removes expired uploads unless a job runs: register `services.AddTusExpiredUploadsCleanup()` (first pass at startup, then every 5 minutes by default; scans the store each pass). It removes expired incomplete uploads only, and requires an `ITusExpirationStore` — `AddTusAzureStore` forwards it automatically; register manually constructed stores with `services.AddSingleton<ITusExpirationStore>(store)`. Expiration itself is enabled via `DefaultTusConfiguration.Expiration`.
- `TusAzureStore` can be constructed manually (`new TusAzureStore(blobServiceClient, options)`) — tusdotnet composes stores inside `DefaultTusConfiguration` factories — or registered via `services.AddTusAzureStore(...)` (overloads: `IConfiguration`, `Action<TusAzureStoreOptions>`, `Action<TusAzureStoreOptions, IServiceProvider>`). The DI path requires a `BlobServiceClient` registration and resolves `ITusAzureBlobHttpHeadersProvider`/`ITusFileIdProvider` when present (TryAdd defaults otherwise); options run through FluentValidation with `ValidateOnStart`.
- For multi-instance deployments, always add `Headless.Tus.DistributedLocks` and call `services.AddDistributedLockTusLockProvider()`. Without it, concurrent PATCH requests for the same file from different nodes can corrupt block lists.
- The distributed lock add-on requires `IDistributedLock` in DI. Register a Headless distributed lock provider first, for example: `services.AddHeadlessDistributedLocks(setup => setup.UseRedis())`.
- Set `FileLockProvider` in your `DefaultTusConfiguration` factory to the `ITusFileLockProvider` resolved from DI — it is not wired automatically. A single-node deployment can omit it and use tusdotnet's in-process lock.
- `BlobMaxChunkSize` must be ≤ 100 MB (a store-imposed memory cap — each chunk is buffered per request; Azure's own per-block limit is 4,000 MiB), and `BlobDefaultChunkSize` must be 1 byte–100 MB and not exceed `BlobMaxChunkSize`. The store auto-selects chunk size from `BlobDefaultChunkSize` vs `BlobMaxChunkSize` based on declared upload length.
- An upload is capped at Azure's 50,000 committed blocks; a too-small chunk size on a very large upload throws a `TusStoreException` at commit time. Increase `BlobDefaultChunkSize`/`BlobMaxChunkSize` to use fewer, larger blocks.
- File ids become blob names: ids from a custom `ITusFileIdProvider` must not contain `/`, `\`, `..`, `,` (the separator used to persist file-id lists in blob metadata), control characters, or leading/trailing whitespace — the store rejects such ids with a `TusStoreException` at every entry point in addition to enforcing the provider's own `ValidateId`.
- `GetExpirationAsync` returns `null` for completed uploads (deliberate `TusDiskStore` divergence): tusdotnet's `FileHasNotExpired` requirement 404s any upload whose expiration passed — reporting the stale timestamp would make completed uploads unreachable via tus (no HEAD, no DELETE/termination) after the sliding window elapses.
- Cancellation-token policy (TusDiskStore parity): client reads use the live request token — it is the disconnect signal — while everything that persists already-received bytes runs on `CancellationToken.None`. `AppendDataAsync` commits the bytes received before a disconnect (the spec: the server "SHOULD always attempt to store as much of the received data as possible"), and `SetExpirationAsync`/`VerifyChecksumAsync` ignore the token entirely: tusdotnet invokes them after the PATCH with the request token already cancelled — exactly when the bookkeeping (sliding-expiration refresh, checksum verify/rollback) must still complete. One exception: the `EnableChunkSplitting=false` fast path for seekable bodies streams directly into `StageBlock` with the live token (tusdotnet request bodies are never seekable; that path is store-direct only).
- The client's `Upload-Metadata` header value is stored verbatim in a single `tus_metadata` blob-metadata entry and echoed byte-for-byte in HEAD responses (TUS round-trip contract). Keys keep their original casing and characters; values may decode to any UTF-8 content. Upload-Metadata that is not printable ASCII or exceeds ~7 KB (Azure's 8 KB blob-metadata cap) is rejected with a `TusStoreException`.
- Checksum verification (SHA1, SHA256, SHA512, MD5) is handled automatically for both `Upload-Checksum` forms. Header flow: blocks are staged but not committed until the checksum passes; mismatched blocks are left uncommitted (Azure auto-removes them after 7 days). Trailer flow (`checksum-trailer`): data commits during the append with a recorded rollback offset (`tus_last_chunk_offset`); `VerifyChecksumAsync` hashes the committed range on demand and rolls the chunk back on mismatch or a faulty/missing trailer.
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

The TUS protocol allows only one concurrent PATCH for a given file, but enforcing this across multiple app instances requires explicit locking. Use **`DistributedLockTusLockProvider`** (from `Headless.Tus.DistributedLocks`): it wraps `IDistributedLock` with `AcquireTimeout = TimeSpan.Zero` and auto-extending leases. Non-blocking — tusdotnet receives `false` from `Lock()` and returns `423 Locked` immediately, and a crashed holder's lease expires so the file is not stuck.

Register it with `services.AddDistributedLockTusLockProvider()` after registering an `IDistributedLock` backend (Redis, SQL Server, …). A single-node deployment can omit it and rely on tusdotnet's in-process lock.

> The store writes blocks to the upload blob without holding an Azure blob lease on it, so do **not** lock that blob directly — a lease on the upload blob would make the store's own writes fail with `412`. Coordinate PATCHes through `DistributedLockTusLockProvider` (a separate lock resource) instead.

---

## Headless.Tus

Base package for the TUS stack: protocol-level helpers every deployment needs regardless of storage provider, plus the shared `tusdotnet` dependency.

### Problem Solved

Two gaps every tus deployment hits regardless of store: browsers hide tus response headers cross-origin (without the right `Access-Control-Expose-Headers`, clients cannot read `Location`/`Upload-Offset` and uploads fail immediately), and nothing removes expired uploads (tusdotnet only reports `Upload-Expires`). Also pins the shared `tusdotnet` + `Headless.Hosting` references so all TUS packages align on one version.

### Key Features

- `TusCorsDefaults` — the tus 1.0.0 CORS surface as constants: `ExposedHeaders`, `AllowedHeaders`, `AllowedMethods` (includes the PATCH/DELETE that default CORS configs miss)
- `CorsPolicyBuilder.WithTusHeaders()` — applies all three in one call; origins/credentials stay the caller's decision
- `TusExpiredUploadsCleanupService` + `AddTusExpiredUploadsCleanup(...)` — background job calling `ITusExpirationStore.RemoveExpiredFilesAsync` on an interval

### Design Notes

Cleanup targets **incomplete** uploads only — conforming Headless stores never report completed uploads as expired, so the job cannot destroy finished data. It binds to the `ITusExpirationStore` capability interface, not a concrete store; store packages forward the registration (`AddTusAzureStore` does), and manually constructed stores register with `services.AddSingleton<ITusExpirationStore>(store)`. The default 5-minute interval trades reclaim latency against the store scan each pass performs. In multi-node deployments every node runs its own loop against the same store: deletions are idempotent so this is safe, but the scan load multiplies and the logged removal counts are per-node — wrap the pass in a distributed-lock single-flight guard if that matters.

### Installation

```bash
dotnet add package Headless.Tus
```

Pulled in transitively by every `Headless.Tus.*` provider package.

### Quick Start

```csharp
using Headless.Tus;

builder.Services.AddCors(options =>
    options.AddPolicy("tus", policy =>
        policy.WithOrigins("https://app.example.com").WithTusHeaders()));

builder.Services.AddTusExpiredUploadsCleanup(); // requires an ITusExpirationStore
```

### Configuration

`TusExpiredUploadsCleanupOptions`:

| Option | Default | Notes |
|---|---|---|
| `Interval` | `5 minutes` | How often expired incomplete uploads are removed. Each pass scans the store's uploads — prefer coarser intervals for large containers. Must be positive. |

### Dependencies

- `tusdotnet`
- `Headless.Hosting`

### Side Effects

- `AddTusExpiredUploadsCleanup` registers a hosted service (`TusExpiredUploadsCleanupService`) and `TimeProvider.System` (TryAdd).
- `TusCorsDefaults` / `WithTusHeaders` are pure helpers — no registrations.

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
- Adaptive chunk sizing: automatic selection between `BlobDefaultChunkSize` (4 MB) and `BlobMaxChunkSize` (16 MB default; up to 100 MB) based on declared upload size
- Pooled buffer stream splitting via `ArrayPool<byte>` to minimize allocations during PATCH ingestion

### Design Notes

**Block blob chunking and checksum deferral.** TUS Checksum verification happens _after_ all PATCH data is staged. `TusAzureStore` stages blocks during `AppendDataAsync` but defers the commit list until `VerifyChecksumAsync` succeeds. The staged block range — a constant-size `token:firstIndex:count` triple that reconstructs the exact block IDs at commit time — and the pre-calculated checksum are written to blob metadata (`tus_last_chunk_blocks`, `tus_last_chunk_checksum`); being constant-size, the tracking cannot approach Azure's 8 KB blob-metadata cap no matter how many blocks one PATCH stages. On success, blocks are committed atomically together with metadata update. On mismatch, blocks are left uncommitted and Azure auto-purges them within 7 days. This means `GetUploadOffsetAsync` reads committed block sizes — not the blob `ContentLength` property — to report accurate offset to resuming clients.

**Blob HTTP header preservation.** Headers returned by `ITusAzureBlobHttpHeadersProvider` are applied at creation and re-supplied on every block-list commit — appends, checksum-verified commits, and rollbacks. Azure's Put Block List clears any `x-ms-blob-*` property omitted from the request, so without the re-supply a custom content type or cache control set at creation would silently reset to `application/octet-stream` on the first PATCH. `ContentHash` is deliberately not carried over: the content changes with each commit, so echoing the stored MD5 would persist a stale digest.

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

var blobServiceClient = new BlobServiceClient(builder.Configuration["Azure:Storage:ConnectionString"]);

var tusStore = new TusAzureStore(
    blobServiceClient,
    new TusAzureStoreOptions
    {
        ContainerName = "uploads",
        BlobPrefix = "tus/",
        CreateContainerIfNotExists = true,
    }
);

var app = builder.Build();

app.MapTus("/files", async _ => new DefaultTusConfiguration { Store = tusStore, UrlPath = "/files" });

app.Run();
```

Serializing concurrent PATCHes across nodes (multi-node deployments) with `Headless.Tus.DistributedLocks`:

```csharp
using Headless.Tus; // AddDistributedLockTusLockProvider
using tusdotnet.Interfaces;

// Register an IDistributedLock backend (Redis, SQL Server, …) first, then the TUS lock adapter:
builder.Services.AddDistributedLockTusLockProvider();

app.MapTus(
    "/files",
    httpContext =>
        Task.FromResult(
            new DefaultTusConfiguration
            {
                Store = tusStore,
                UrlPath = "/files",
                FileLockProvider = httpContext.RequestServices.GetRequiredService<ITusFileLockProvider>(),
            }
        )
);
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
| `BlobMaxChunkSize` | `16 MB` | Block size used for uploads ≥ 100 MB; also the per-request memory buffering unit for large uploads. Max 100 MB — a store-imposed memory cap; Azure's own per-block limit is 4,000 MiB. |
| `DeletePartialFilesOnConcat` | `false` | Delete partial uploads after a final upload is committed (best-effort; keep `false` if clients reuse partials). |

Chunk size selection logic: uploads < 10 MB use `min(BlobDefaultChunkSize, fileSize)`; uploads 10–100 MB use `BlobDefaultChunkSize`; uploads ≥ 100 MB use `BlobMaxChunkSize`.

### Dependencies

- `Headless.Tus`
- `Azure.Storage.Blobs`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

- Synchronously calls `BlobContainerClient.CreateIfNotExists` during `TusAzureStore` construction when `CreateContainerIfNotExists = true`.
- DI registration is optional: `TusAzureStore` can be constructed manually or registered via `AddTusAzureStore` (requires a `BlobServiceClient` in DI; container creation still runs synchronously at first resolution when enabled). For cross-node PATCH locking, register `Headless.Tus.DistributedLocks`.

---

## Headless.Tus.DistributedLocks

Distributed lock-based TUS file lock provider, using `Headless.DistributedLocks` to prevent concurrent PATCH corruption across multiple application instances.

### Problem Solved

The TUS protocol allows only one concurrent PATCH per file. On single-instance deployments, `tusdotnet`'s default in-process locking suffices. On multi-instance deployments (load-balanced or Kubernetes pods), each instance has its own in-process lock table, so two nodes can simultaneously PATCH the same file, producing interleaved blocks and corrupted uploads. `DistributedLockTusLockProvider` uses the framework's `IDistributedLock` to coordinate across nodes.

### Key Features

- `DistributedLockTusLockProvider` — `ITusFileLockProvider` backed by `IDistributedLock`
- `DistributedLockTusFileLock` — `ITusFileLock` that calls `TryAcquireAsync` with zero wait; returns `false` immediately if another node holds the lock (tusdotnet returns `423 Locked` to the client)
- Lock resource key format: `{resourcePrefix}-{fileId}` with prefix default `tus-file-lock`; give each TUS endpoint its own prefix when several endpoints (different stores/containers) share one `IDistributedLock` backend, so equal file ids cannot contend for the same lock
- Compatible with any `IDistributedLock` backend (Redis, in-memory, etc.)
- Single `AddDistributedLockTusLockProvider(resourcePrefix?)` extension on `IServiceCollection`
- Best-effort mutual exclusion, not fencing: tusdotnet's `ITusFileLock` contract has no hook to observe a lease lost mid-request, so a holder that loses its lease during a backend partition keeps writing until the request ends — the auto-extending lease shrinks that window but cannot eliminate it

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
app.MapTus(
    "/files",
    async ctx =>
    {
        var lockProvider = ctx.RequestServices.GetRequiredService<ITusFileLockProvider>();

        return new DefaultTusConfiguration
        {
            Store = tusStore, // your TusAzureStore instance
            UrlPath = "/files",
            FileLockProvider = lockProvider,
        };
    }
);

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
