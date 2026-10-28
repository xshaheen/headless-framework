---
domain: TUS (Resumable Uploads)
packages: Tus, Tus.Azure, Tus.S3, Tus.DistributedLocks
---

# TUS (Resumable Uploads)

> TUS protocol implementation for resumable file uploads, with Azure Blob Storage and S3-compatible (Cloudflare R2, Amazon S3, MinIO) stores and distributed lock support.

## Orientation

Four packages compose the TUS domain:

| Package | Role |
|---|---|
| `Headless.Tus` | Base package: protocol-level pieces every deployment needs — `TusCorsDefaults`/`WithTusHeaders()` for browser clients, `AddTusExpiredUploadsCleanup()` for expired-upload removal — plus the shared `tusdotnet` + `Headless.Hosting` references. |
| `Headless.Tus.Azure` | Storage backend: `TusAzureStore` stores upload chunks in Azure Blob Storage using block blobs. |
| `Headless.Tus.S3` | Storage backend: `TusS3Store` stores uploads as S3 multipart uploads in Cloudflare R2, Amazon S3, or an S3-compatible server, through the app's `IAmazonS3` client. |
| `Headless.Tus.DistributedLocks` | Locking add-on: `DistributedLockTusLockProvider` prevents concurrent PATCH corruption across nodes. |

`Headless.Tus` has no store — pair it with one store package: `Headless.Tus.Azure` or `Headless.Tus.S3` ([Choosing a store](#choosing-a-store)). Add `Headless.Tus.DistributedLocks` for multi-instance deployments.

A complete resumable-upload backend on Azure (Azurite for local development: `docker run -d -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0`):

```csharp
using Headless.Tus;
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

The S3 store swaps steps 1 and 2 for an `IAmazonS3` registration and `AddTusS3Store`, and resolves `TusS3Store` in step 5 ([Headless.Tus.S3](#headlesstuss3)).

Any tus 1.0.0 client works against this endpoint (`tus-js-client`, Uppy's `@uppy/tus`, tuspy, …). A complete runnable example — this backend plus a React frontend driving the endpoint with both Uppy Dashboard and `use-tus` — lives in [demo/Headless.Tus.Demo](../../demo/Headless.Tus.Demo/README.md).

## Agent Rules

- `Headless.Tus` has no store implementation — add exactly one store package for upload storage: `Headless.Tus.Azure` or `Headless.Tus.S3`. It does ship the cross-provider pieces: `TusCorsDefaults` / `WithTusHeaders()` and `AddTusExpiredUploadsCleanup()`.
- Browser clients on another origin need the tus CORS surface: use `policy.WithTusHeaders()` (allowed request headers + exposed response headers + methods incl. PATCH/DELETE). Without the exposed headers, `tus-js-client`/Uppy cannot read `Location`/`Upload-Offset` and uploads fail on the first request.
- Nothing removes expired uploads unless a job runs: register `services.AddTusExpiredUploadsCleanup()` (first pass at startup, then every 5 minutes by default; scans the store each pass). It removes expired incomplete uploads only, and requires an `ITusExpirationStore` — `AddTusAzureStore` and `AddTusS3Store` forward it automatically; register manually constructed stores with `services.AddSingleton<ITusExpirationStore>(store)`. Expiration itself is enabled via `DefaultTusConfiguration.Expiration`. The reaper runs outside tusdotnet's middleware and holds no `ITusFileLock`, so each store deletes an expired upload only if it is unchanged since the pass found it expired (`TusAzureStore` checks the blob ETag, `TusS3Store` the state object's ETag): a resume that committed data or refreshed the sliding expiration in between makes the delete skip that upload (not counted, not an error). A resume that has begun but not yet written anything can still be deleted mid-resume — gate uploads and cleanup with `Headless.Tus.DistributedLocks` to eliminate that window.
- `TusAzureStore` can be constructed manually (`new TusAzureStore(blobServiceClient, options)`) — tusdotnet composes stores inside `DefaultTusConfiguration` factories — or registered via `services.AddTusAzureStore(...)` (overloads: `IConfiguration`, `Action<TusAzureStoreOptions>`, `Action<TusAzureStoreOptions, IServiceProvider>`). The DI path requires a `BlobServiceClient` registration and resolves `ITusAzureBlobHttpHeadersProvider`/`ITusFileIdProvider` when present (TryAdd defaults otherwise); options run through FluentValidation with `ValidateOnStart`.
- `TusAzureStore` rejects a write from a PATCH whose view of the blob is stale (it lost its file lock and another PATCH committed meanwhile) with `Azure.RequestFailedException` status `412`, deliberately not a `TusStoreException`. Through tusdotnet it surfaces as an unhandled exception, so the server answers `500` and sends no `Upload-Offset`; tus clients retry a 5xx by issuing HEAD and resuming from the committed offset. Do not map that exception to `400` (or any non-retryable 4xx) in an exception handler: tus clients treat `400` as fatal. Mapping it to `409` keeps it retryable.
- For multi-instance deployments, always add `Headless.Tus.DistributedLocks` and call `services.AddDistributedLockTusLockProvider()`. Without it, concurrent PATCH requests for the same file from different nodes interleave on one upload: on Azure they can corrupt block lists, and on S3 the write fencing fails all but one of them with a 5xx the client has to retry.
- The distributed lock add-on requires `IDistributedLock` in DI. Register a Headless distributed lock provider first, for example: `services.AddHeadlessDistributedLocks(setup => setup.UseRedis())`.
- Set `FileLockProvider` in your `DefaultTusConfiguration` factory to the `ITusFileLockProvider` resolved from DI — it is not wired automatically. A single-node deployment can omit it and use tusdotnet's in-process lock.
- `BlobMaxChunkSize` must be ≤ 100 MB (a store-imposed memory cap — each chunk is buffered per request; Azure's own per-block limit is 4,000 MiB), and `BlobDefaultChunkSize` must be 1 byte–100 MB and not exceed `BlobMaxChunkSize`. The store auto-selects chunk size from `BlobDefaultChunkSize` vs `BlobMaxChunkSize` based on declared upload length.
- With `EnableChunkSplitting = false` the whole PATCH body is buffered in memory and staged as one Azure block. `MaxNoSplitBufferSize` (default 256 MB, 1 byte–~2 GB) caps that buffer and is the *only* in-flight memory bound for a Creation-Defer-Length no-split upload — its declared length is unknown, so the too-much-data guard is a no-op (and tusdotnet disables Kestrel's PATCH body limit). A body exceeding the cap is rejected with a `TusStoreException` (HTTP 400 through the pipeline). For a declared length, tusdotnet rejects an over-length PATCH itself with 413 before the store buffers anything. Ignored when splitting is enabled.
- An upload is capped at Azure's 50,000 committed blocks; a too-small chunk size on a very large upload throws a `TusStoreException` at commit time. Increase `BlobDefaultChunkSize`/`BlobMaxChunkSize` to use fewer, larger blocks.
- File ids become blob names: ids from a custom `ITusFileIdProvider` must not contain `/`, `\`, `..`, `,` (the separator used to persist file-id lists in blob metadata), control characters, or leading/trailing whitespace — the store rejects such ids with a `TusStoreException` at every entry point in addition to enforcing the provider's own `ValidateId`.
- `GetExpirationAsync` returns `null` for completed uploads (deliberate `TusDiskStore` divergence): tusdotnet's `FileHasNotExpired` requirement 404s any upload whose expiration passed — reporting the stale timestamp would make completed uploads unreachable via tus (no HEAD, no DELETE/termination) after the sliding window elapses.
- Cancellation-token policy (TusDiskStore parity): client reads use the live request token — it is the disconnect signal — while everything that persists already-received bytes runs on `CancellationToken.None`. `AppendDataAsync` commits the bytes received before a disconnect (the spec: the server "SHOULD always attempt to store as much of the received data as possible"), and `SetExpirationAsync`/`VerifyChecksumAsync` ignore the token entirely: tusdotnet invokes them after the PATCH with the request token already cancelled — exactly when the bookkeeping (sliding-expiration refresh, checksum verify/rollback) must still complete. One exception: the `EnableChunkSplitting=false` fast path for seekable bodies streams directly into `StageBlock` with the live token (tusdotnet request bodies are never seekable; that path is store-direct only). Expired-file deletion likewise runs on `CancellationToken.None` once a file is enumerated, and a cancelled enumeration returns the files found so far (`TusDiskStore` parity). One deliberate divergence: a checksum PATCH whose chunk was fully received and hashed just before the disconnect is *committed* (its digest verifies), whereas `TusDiskStore` discards any chunk from a disconnected request — keeping verified bytes serves the spec better, and the resuming client sees the advanced offset on HEAD.
- The client's `Upload-Metadata` header value is stored verbatim in a single `tus_metadata` blob-metadata entry and echoed byte-for-byte in HEAD responses (TUS round-trip contract). Keys keep their original casing and characters; values may decode to any UTF-8 content. Upload-Metadata that is not printable ASCII or exceeds ~7 KB (Azure's 8 KB blob-metadata cap) is rejected with a `TusStoreException`. A concatenation *final* also persists `tus_partial_uploads` (comma-joined constituent ids) alongside the verbatim metadata, so `CreateFinalFileAsync` re-checks the composed total against the 8 KB cap and throws a `TusStoreException` rather than an opaque Azure 400 when many partials plus near-cap metadata would overflow it.
- Checksum verification (SHA1, SHA256, SHA512, MD5) is handled automatically for both `Upload-Checksum` forms. Header flow: blocks are staged but not committed until the checksum passes; mismatched blocks are left uncommitted (Azure auto-removes them after 7 days). Trailer flow (`checksum-trailer`): data commits during the append with a recorded rollback offset (`tus_last_chunk_offset`); `VerifyChecksumAsync` hashes the committed range on demand and rolls the chunk back on mismatch or a faulty/missing trailer.
- The concatenation extension uses server-side block copy (`StageBlockFromUri`) when available, falling back to streaming when it is not — Azurite returns HTTP 501, and a private container returns HTTP 403 for the bare source URI (no SAS).
- `TusAzureStore` never creates its container; the constructor makes no network call. `AddTusAzureStore` registers the container as a [provisioned resource](utilities.md#resource-provisioning) of kind `AzureBlob`, named after `ContainerName`: the deployment runs `dotnet <app>.dll resources provision` (or `deploy`), which creates it with `ContainerPublicAccessType`, and a starting host only checks that it exists, holding the `warmup` readiness check until it does. A development or test host calls `services.AddDeployStepsOnStartup()` to create it as the host starts. A manually constructed store has no provisioner, so create its container beforehand. `resources purge` deletes the blobs under `BlobPrefix` and keeps the container; `resources delete` deletes them too, then the container only when nothing else is left in it, since a container can hold blobs outside `BlobPrefix`.
- `ITusAzureBlobHttpHeadersProvider` lets you inject custom `Content-Type` / cache headers per file. Default sets `application/octet-stream`.
- `AddTusS3Store` takes `IAmazonS3` from DI and does not configure it. For Cloudflare R2, register the client with path style, `AuthenticationRegion = "auto"`, and `WHEN_REQUIRED` request and response checksums (R2 rejects the SDK's default CRC checksums), and set `Provider = TusS3Provider.R2`. See [Headless.Tus.S3](#headlesstuss3) for one client example per server.
- `TusS3Store` does not support the Checksum extension, and an incomplete upload's content is not readable (`GetContentAsync` throws until the upload completes). On R2, an upload must finish within 7 days of its first part, when R2 aborts the multipart upload; keep the tus expiration below that.
- A deferred-length upload on `TusS3Store` can grow to `(10,000 − 4 × PartClaimWindow) × PartSize` bytes, 77,952 MiB (about 76 GiB) at the defaults; a known length up to `(10,000 − 4 × PartClaimWindow) × MaxPartSize`, 623,616 MiB (about 609 GiB). Raise `PartSize` for larger deferred uploads.
- `TusS3Store` never creates its bucket and never edits bucket lifecycle rules. Run `resources provision -k S3Bucket` as a deploy step; on R2 that step needs a token that can create buckets, while the serving host needs only object rights.

## Core Concepts

### TUS Protocol Flow

TUS is an HTTP-based protocol for resumable uploads. The key operations are:

1. **POST /files** — Upload creation. Client declares `Upload-Length` (or defers it). Server returns a `Location` URL with a file ID and `Upload-Offset: 0`.
2. **PATCH /files/{id}** — Chunk upload. Client sends bytes starting at `Upload-Offset`. Server appends and returns the new offset.
3. **HEAD /files/{id}** — Resume check. Client fetches current `Upload-Offset` to know where to resume after a disconnect.
4. **DELETE /files/{id}** (Termination extension) — Cancels and cleans up.

`tusdotnet` handles the HTTP negotiation; the store (`TusAzureStore` or `TusS3Store`) handles where bytes land.

### Choosing a store

| Store | Use when | Trade-off |
|---|---|---|
| `Headless.Tus.Azure` | Uploads land in Azure Blob Storage, or clients send `Upload-Checksum`. | Supports every tus extension tusdotnet defines, Checksum included. |
| `Headless.Tus.S3` | Uploads land in Cloudflare R2, Amazon S3, or an S3-compatible server. | No Checksum extension. An incomplete upload's content is not readable, only its metadata. On R2 an upload must finish within 7 days of its first part. |

Both stores fence every write against stale writers, report completed uploads as never expiring, and register their storage as a [provisioned resource](utilities.md#resource-provisioning). Moving from one to the other means swapping the client registration and the `AddTus*Store` call; uploads in flight do not move.

### Azure Block Blob Mapping

Azure Block Blobs decompose a file into independently-uploadable blocks that are committed in a single atomic operation. `TusAzureStore` maps TUS chunks onto Azure blocks:

- Each PATCH appends one or more staged blocks (split by `BlobDefaultChunkSize` / `BlobMaxChunkSize`).
- A block list commit makes the blocks durable and visible as blob content.
- For checksum uploads, blocks are staged but the commit is deferred until `VerifyChecksumAsync` passes — if checksum fails, staged blocks are left uncommitted and Azure discards them within 7 days.

This is why `GetUploadOffsetAsync` sums committed block sizes, not the blob's `ContentLength` property.

### Concurrent PATCH Safety

The TUS protocol allows only one concurrent PATCH for a given file, but enforcing this across multiple app instances requires explicit locking. Use **`DistributedLockTusLockProvider`** (from `Headless.Tus.DistributedLocks`): it wraps `IDistributedLock` with `AcquireTimeout = TimeSpan.Zero` and auto-extending leases. Non-blocking — tusdotnet receives `false` from `Lock()` and returns `423 Locked` immediately, and a crashed holder's lease expires so the file is not stuck.

Register it with `services.AddDistributedLockTusLockProvider()` after registering an `IDistributedLock` backend (Redis, SQL Server, …). A single-node deployment can omit it and rely on tusdotnet's in-process lock.

The lock is best-effort: a holder can lose it without noticing (a process pause, a lock-backend failover) while a second PATCH proceeds. Both stores therefore also fence their writes. `TusAzureStore` conditions every block-list commit and metadata write on the blob ETag its request read, and `TusS3Store` conditions every write of an upload's state object on its ETag, so the stale PATCH's write fails instead of overwriting the other PATCH's data (see the **Write fencing** design constraints under [`Headless.Tus.Azure`](#headlesstusazure) and [`Headless.Tus.S3`](#headlesstuss3)).

> The store writes blocks to the upload blob without holding an Azure blob lease on it, so do **not** lock that blob directly — a lease on the upload blob would make the store's own writes fail with `412`. Coordinate PATCHes through `DistributedLockTusLockProvider` (a separate lock resource) instead.

---

## Headless.Tus

Base package for the TUS stack: protocol-level helpers every deployment needs regardless of storage provider, plus the shared `tusdotnet` dependency.

### API and behavior

- `TusCorsDefaults` — the tus 1.0.0 CORS surface as constants: `ExposedHeaders`, `AllowedHeaders`, `AllowedMethods` (includes the PATCH/DELETE that default CORS configs miss)
- `CorsPolicyBuilder.WithTusHeaders()` — applies all three in one call; origins/credentials stay the caller's decision
- `AddTusExpiredUploadsCleanup(...)` — registers an internal background hosted service calling `ITusExpirationStore.RemoveExpiredFilesAsync` on an interval (tuned via the public `TusExpiredUploadsCleanupOptions`)

### Design constraints

Cleanup targets **incomplete** uploads only — conforming Headless stores never report completed uploads as expired, so the job cannot destroy finished data. It binds to the `ITusExpirationStore` capability interface, not a concrete store; store packages forward the registration (`AddTusAzureStore` and `AddTusS3Store` do), and manually constructed stores register with `services.AddSingleton<ITusExpirationStore>(store)`. The default 5-minute interval trades reclaim latency against the store scan each pass performs. In multi-node deployments every node runs its own loop against the same store: deletions are idempotent so this is safe, but the scan load multiplies and the logged removal counts are per-node — wrap the pass in a distributed-lock single-flight guard if that matters.

### Install

```bash
dotnet add package Headless.Tus
```

Pulled in transitively by every `Headless.Tus.*` provider package.

### Setup and use

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

### Runtime behavior

- `AddTusExpiredUploadsCleanup` registers an internal hosted service and `TimeProvider.System` (TryAdd).
- `TusCorsDefaults` / `WithTusHeaders` are pure helpers — no registrations.

---

## Headless.Tus.Azure

Azure Blob Storage TUS store implementation.

### API and behavior

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

### Design constraints

**Block blob chunking and checksum deferral.** TUS Checksum verification happens _after_ all PATCH data is staged. `TusAzureStore` stages blocks during `AppendDataAsync` but defers the commit list until `VerifyChecksumAsync` succeeds. The staged block range — a constant-size `token:firstIndex:count` triple that reconstructs the exact block IDs at commit time — and the pre-calculated checksum are written to blob metadata (`tus_last_chunk_blocks`, `tus_last_chunk_checksum`); being constant-size, the tracking cannot approach Azure's 8 KB blob-metadata cap no matter how many blocks one PATCH stages. On success, blocks are committed atomically together with metadata update. On mismatch, blocks are left uncommitted and Azure auto-purges them within 7 days. This means `GetUploadOffsetAsync` reads committed block sizes — not the blob `ContentLength` property — to report accurate offset to resuming clients.

**Blob HTTP header preservation.** Headers returned by `ITusAzureBlobHttpHeadersProvider` are applied at creation and re-supplied on every block-list commit — appends, checksum-verified commits, and rollbacks. Azure's Put Block List clears any `x-ms-blob-*` property omitted from the request, so without the re-supply a custom content type or cache control set at creation would silently reset to `application/octet-stream` on the first PATCH. `ContentHash` is deliberately not carried over: the content changes with each commit, so echoing the stored MD5 would persist a stale digest.

**Write fencing.** Every block-list commit and metadata write — append commits, checksum staging and verified commits, rollbacks and discards, `SetUploadLengthAsync`, and `SetExpirationAsync` — sends the ETag the calling request read as `If-Match` and carries each write's returned ETag into its next write, so a request's own sequential writes keep matching while a request whose view is stale gets `412`. A stale writer fails with `Azure.RequestFailedException` (status `412`), not `TusStoreException`, so tusdotnet does not answer `400` and tus clients retry and resume (see Agent Rules). `SetExpirationAsync` is the exception that retries: it merges one field into the blob's current metadata, so it re-reads and re-applies up to three times rather than failing a PATCH whose data already committed. Each store call fences from its own read: in the checksum header flow, `VerifyChecksumAsync` re-reads the blob, and its digest comparison is what rejects blocks a different writer staged in between. The concatenated final blob is committed with `If-None-Match: *`, so a new final upload can never replace an existing blob. A tus `DELETE` and the post-concatenation deletion of partials stay unconditional (the client asked for removal, and a concatenated partial is consumed whatever its state); the expired-upload reaper's deletes are conditional (see Agent Rules).

**Server-side concatenation.** Final-file creation uses `StageBlockFromUri` to copy block ranges across blobs without moving data through the application server. When server-side copy is unavailable — Azurite returns HTTP 501, and a private container returns HTTP 403 because the source blob is not readable via its bare URI (no SAS) — the store falls back to download-then-re-upload streaming automatically.

**Container provisioning.** Creating the container is a deploy step, not store construction: the `AzureBlob` provisioner that `AddTusAzureStore` registers creates it on `resources provision`, so the running host needs no container-create permission. At start the host verifies the container with one existence check (`ResourceStartupCheck.GateReadiness`); override it per kind with `ResourceProvisioningOptions.StartupChecks`.

### Install

```bash
dotnet add package Headless.Tus.Azure
```

### Setup and use

```csharp
using Azure.Storage.Blobs;
using Headless.Tus;
using tusdotnet;
using tusdotnet.Models;

var builder = WebApplication.CreateBuilder(args);

var blobServiceClient = new BlobServiceClient(builder.Configuration["Azure:Storage:ConnectionString"]);

// A manually constructed store does not create its container: create "uploads" in the deployment,
// or register the store with AddTusAzureStore and run `resources provision`.
var tusStore = new TusAzureStore(
    blobServiceClient,
    new TusAzureStoreOptions
    {
        ContainerName = "uploads",
        BlobPrefix = "tus/",
    }
);

var app = builder.Build();

app.MapTus("/files", async _ => new DefaultTusConfiguration { Store = tusStore, UrlPath = "/files" });

app.Run();
```

Serializing concurrent PATCHes across nodes (multi-node deployments) with `Headless.Tus.DistributedLocks`:

```csharp
using Headless.Tus; // AddDistributedLockTusLockProvider
using tusdotnet;
using tusdotnet.Interfaces;
using tusdotnet.Models;

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
using Headless.Tus;

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
| `ContainerPublicAccessType` | `PublicAccessType.None` | Access type `resources provision` applies when it creates the container; ignored for an existing one. |
| `EnableChunkSplitting` | `true` | Splits large PATCH bodies into multiple Azure blocks. |
| `BlobDefaultChunkSize` | `4 MB` | Default block size for medium uploads. Must be 1 byte–100 MB and not exceed `BlobMaxChunkSize`. |
| `BlobMaxChunkSize` | `16 MB` | Block size used for uploads ≥ 100 MB; also the per-request memory buffering unit for large uploads. Max 100 MB — a store-imposed memory cap; Azure's own per-block limit is 4,000 MiB. |
| `DeletePartialFilesOnConcat` | `false` | Delete partial uploads after a final upload is committed (best-effort; keep `false` if clients reuse partials). |

Chunk size selection logic: uploads < 10 MB use `min(BlobDefaultChunkSize, fileSize)`; uploads 10–100 MB use `BlobDefaultChunkSize`; uploads ≥ 100 MB use `BlobMaxChunkSize`.

### Runtime behavior

- Construction makes no network call; the container must already exist (see Container provisioning).
- DI registration is optional: `TusAzureStore` can be constructed manually or registered via `AddTusAzureStore` (requires a `BlobServiceClient` in DI, and registers the container provisioner). For cross-node PATCH locking, register `Headless.Tus.DistributedLocks`.

---

## Headless.Tus.S3

TUS store for Cloudflare R2, Amazon S3, and S3-compatible servers such as MinIO. It writes each upload as one S3 multipart upload and takes the S3 client from the app.

### API and behavior

- `TusS3Store` — `ITusStore` implementation over the S3 API; also implements `ITusPipelineStore`. The `PipeReader` and `Stream` appends store the same offsets with the same durability.
- TUS extensions supported:
  - **Creation** (`ITusCreationStore`), including creation-with-upload
  - **CreationDeferLength** (`ITusCreationDeferLengthStore`), up to a capped length (see **Part size and length limits**)
  - **Concatenation** (`ITusConcatenationStore`) — partials of any size; parts inside one partial are copied server-side with a ranged `UploadPartCopy`
  - **Expiration** (`ITusExpirationStore`)
  - **Termination** (`ITusTerminationStore`) — removes the multipart upload, the state object, the tail segments, and the finished object
  - **Readable** (`ITusReadableStore`) — `GetFileAsync` returns `ITusFile`
- **Checksum is not supported.** `TusS3Store` does not implement `ITusChecksumStore`, so tusdotnet does not advertise the extension. Use `Headless.Tus.Azure` when clients must send `Upload-Checksum`.
- `AddTusS3Store(...)` — overloads: `IConfiguration`, `Action<TusS3StoreOptions>`, `Action<TusS3StoreOptions, IServiceProvider>`. It resolves `IAmazonS3` from DI with `GetRequiredService`, honors an `ITusS3ObjectHeadersProvider` or `ITusFileIdProvider` registered before the call, forwards `ITusExpirationStore`, adds `IHttpContextAccessor`, and registers the bucket as a provisioned resource of kind `S3Bucket`.
- `new TusS3Store(s3, options, objectHeadersProvider, fileIdProvider, loggerFactory, timeProvider, httpContextAccessor)` — every parameter after `options` is optional. The constructor validates the options and throws `ValidationException` on invalid ones.
- `TusS3Provider` — `Aws` (default), `S3Compatible`, `R2`; selects the per-request settings (see **Providers and the S3 client**).
- `ITusS3ObjectHeadersProvider` / `DefaultTusS3ObjectHeadersProvider` / `TusS3ObjectHeaders` — the `Content-Type`, `Cache-Control`, `Content-Disposition`, `Content-Encoding`, and `Content-Language` the finished object carries.

### Design constraints

**Providers and the S3 client.** The app registers `IAmazonS3`; the store never builds one. Client-level settings are the app's to set, and registration does not check them:

| Server | `ServiceURL` | `ForcePathStyle` | `AuthenticationRegion` | Checksum settings | `Provider` |
|---|---|---|---|---|---|
| Cloudflare R2 | `https://<account-id>.r2.cloudflarestorage.com` | `true` | `auto` | `WHEN_REQUIRED` (required) | `R2` |
| Amazon S3 | SDK default (region endpoint) | SDK default | SDK default | SDK default | `Aws` |
| MinIO or another S3-compatible server | the server URL, `http` allowed | `true` | the server's region, often `us-east-1` | `WHEN_REQUIRED` (most servers reject the SDK's default CRC checksums) | `S3Compatible` |

R2 rejects the CRC checksums AWS SDK v4 adds to every request by default, so an R2 client must set `RequestChecksumCalculation` and `ResponseChecksumValidation` to `WHEN_REQUIRED`. `Provider` changes only per-request settings. Every object and part write sends a plain `Content-Length` body (`UseChunkEncoding = false`) for all three. `R2` sends unsigned payloads (`DisablePayloadSigning = true`), which skips a hash pass over every part. `S3Compatible` keeps payloads signed, because the SDK refuses unsigned payloads over plain `http`. `Aws` keeps the SDK's signing default. `R2` also sets the default `MultipartUploadLifetime` to 7 days.

R2 behavior is tested against a real R2 bucket in the release checks. Amazon S3 behavior is verified against LocalStack, not a real AWS account. MinIO is supported as an S3-compatible endpoint; its upstream repository was archived in April 2026.

**Object layout.** The store owns every key under `Prefix` (default `uploads/`), so give it a prefix no other writer uses: `resources purge` deletes objects there. An upload with file id `{id}` uses:

- `{Prefix}{id}` — the finished object, written when the upload completes.
- `{Prefix}.tus/{id}/info` — the JSON state object of an incomplete upload: length, part size, metadata, expiry, multipart upload id, committed parts and tail segments.
- `{Prefix}.tus/{id}/seg/...` — tail segments: received bytes that do not yet fill a part.
- `{Prefix}.tus-completed/{id}` — the record a completed upload keeps beside its finished object.

The tus metadata lives in the state object, not in S3 object metadata, so S3's 2 KB user-metadata limit does not apply. File ids become keys: the store rejects an id that starts with `.`, contains `/`, `\`, or a control character, or starts or ends with whitespace, with a `TusStoreException`.

**Write fencing.** Creation writes the state object with `If-None-Match: *`, and every later write sends `If-Match` with the ETag the request read. A request whose view is stale (it lost its file lock and another request wrote meanwhile) fails with `AmazonS3Exception` (`412 PreconditionFailed`, `409 ConditionalRequestConflict`, or `404 NoSuchKey`), deliberately not a `TusStoreException`. tusdotnet answers `500`, and the tus client resumes from HEAD. Do not map that exception to `400` or another non-retryable 4xx. Before uploading parts, a PATCH reserves a window of part numbers (`PartClaimWindow`) with one conditional state write, so a stale writer fails before it touches a part. This matters on R2, which replaces a part uploaded again under the same number and loses it when that upload fails. No step relies on a conditional delete.

**Part size and length limits.** S3 and R2 allow 10,000 parts per multipart upload, and R2 requires every part except the last to be the same size. The store fixes each upload's part size at creation, and every part except the last is exactly that size. Four claim windows of part numbers stay in reserve for crash recovery, so an upload spans at most `10,000 − 4 × PartClaimWindow` parts: 9,744 at the default window of 64.

- **Known length.** The part size is the larger of `PartSize` and the length divided by the part budget, rounded up to 1 MiB. The largest accepted length is the part budget times `MaxPartSize`: 9,744 × 64 MiB = 623,616 MiB (about 609 GiB) at the defaults. Creation rejects a longer upload with a `TusStoreException` (`400`).
- **Deferred length.** The upload uses `PartSize`, so it can grow to the part budget times `PartSize`: 9,744 × 8 MiB = 77,952 MiB (about 76 GiB) at the defaults. Declaring a longer length, or appending past the cap, fails with a `TusStoreException`. Raise `PartSize` to raise the cap; it also raises the memory each PATCH holds.

**Tail segments and memory.** Bytes that do not fill a part are written as a new tail segment object, so a client may send chunks of any size, below 5 MiB included. The store reads the segments back into the next part once enough bytes arrive, and deletes them only after the state write that records that part, so no step drops bytes the store already reported in an offset. Past `MaxTailSegments` segments, the store folds them into one. Each PATCH holds at most `MaxConcurrentPartUploads` + 1 part buffers, taken from a pool sized to `MaxPartSize`; the store writes nothing to local disk.

**Disconnects.** When the client disconnects, the store still stores every byte it received: it commits the parts that landed and writes the rest as tail segments, bounded by `PersistTimeout`. Keep `PersistTimeout` below the lease duration of the `IDistributedLock` backend behind `Headless.Tus.DistributedLocks`. Otherwise a lock failover can start a second writer while the first is still persisting; the write fencing then fails one of them.

**Completion.** The final PATCH uploads the last part, marks the upload as completing with one conditional write, completes the multipart upload (or writes the finished object in one request when the upload never filled a part), writes the completed record, and deletes the state object. A zero-length upload writes an empty finished object at creation. If completion is interrupted, HEAD reports the offset from before the final PATCH, the client sends that PATCH again, and the store finishes the completion and tusdotnet raises `OnFileCompleteAsync`. Concatenation builds the final upload inside `CreateFinalFileAsync`, writes its completed record last, and keeps the partial uploads.

**Aborted multipart uploads.** By default R2 aborts every incomplete multipart upload 7 days after it was created. With `Provider = R2` and no `MultipartUploadLifetime`, the store applies the same 7 days: once they pass since the upload's first part, the upload reads as missing (`404`) and the cleanup job removes it. A client must therefore finish an R2 upload within 7 days of its first part; set the tus expiration below that. When the provider reports the multipart upload gone (`NoSuchUpload`), the store marks the upload gone and answers with a `TusStoreException`, never a corrupt upload. The store never edits bucket lifecycle rules; if a rule changes R2's window, set `MultipartUploadLifetime` to match. On AWS or an S3-compatible server, an abort-incomplete-multipart-upload lifecycle rule is the operator's choice; when the bucket has one, set `MultipartUploadLifetime` to its window.

**Bucket provisioning.** Creating the bucket is a deploy step. `AddTusS3Store` registers a provisioner of kind `S3Bucket` named after `Bucket`: `dotnet <app>.dll resources provision -k S3Bucket` (or `deploy`) creates it, and a bucket the account already owns counts as created. A starting host only verifies it with one `ListObjectsV2` call (`ResourceStartupCheck.GateReadiness`): the host starts, and the `warmup` readiness check stays unhealthy until the bucket exists. On R2, the deploy step needs an API token that can create buckets, while the serving host needs only object read, write, and list rights. `resources purge` deletes the working sets, the completed records, and the finished objects those records name, and keeps the bucket and every other object. `resources delete` does the same, then deletes the bucket only when nothing else is left in it. A manually constructed store has no provisioner, so create its bucket beforehand.

**Storage requests.** The DI registration lets the store read an upload's state once per tus request, though tusdotnet calls several store methods per request. A store constructed without an `IHttpContextAccessor` reads the state on every call and stays correct.

| tus operation | Storage requests |
|---|---|
| HEAD, incomplete upload | 1 |
| HEAD, completed upload | 2 |
| Creation | 1, plus 1 when expiration is on |
| PATCH that only adds a tail segment | 3 |
| PATCH that writes N parts | N + 4 + ⌈N / 64⌉, plus 1 per tail segment read back, plus 1 on the upload's first part-writing PATCH (creates the multipart upload) |
| Final PATCH, on top of its append | 3 (mark completing, complete, write the record), plus 1 delete |
| Expiry sweep | 1 listing per 1,000 in-progress uploads and 1 read per in-progress upload; a removal pass adds 1 multipart-upload listing page and the deletes for each removed upload. Completed uploads cost nothing. |

Under sliding expiration, the first PATCH that writes after creation costs one more state write to refresh the expiry; later PATCHes carry the refresh in their commit.

**Expiry sweep.** `RemoveExpiredFilesAsync` lists the in-progress uploads under `{Prefix}.tus/`, one entry per upload, and reads each state object. Before removing an expired or gone upload it writes the state as gone with `If-Match`, and skips the upload when a request changed it since the read. It also aborts a multipart upload older than one hour that sits on the key of an upload it read but that upload's state does not record (a leak from a crash or a stale writer), and never touches a multipart upload on any other key. Known limit: MinIO lists multipart uploads only under an exact object key, so on MinIO the sweep finds no leaked multipart uploads. Termination lists the exact key and still aborts them; a bucket abort rule covers the rest.

**Readable store.** `GetFileAsync` returns `null` for an unknown or gone upload. For an incomplete upload it returns the metadata, but `GetContentAsync` throws `InvalidOperationException`, because S3 does not serve the parts of an incomplete multipart upload. Inside `OnFileCompleteAsync` the upload is complete and `GetContentAsync` streams the finished object.

**Object headers.** The finished object's headers are fixed when its multipart upload starts or its single write happens, and S3 cannot change them later without copying the object. `DefaultTusS3ObjectHeadersProvider` sets `Content-Type` from the `filetype` metadata when it parses as a media type, otherwise `application/octet-stream`, and sets no other header.

### Install

```bash
dotnet add package Headless.Tus.S3
```

### Setup and use

Register `IAmazonS3` for the server, then the store. Cloudflare R2:

```csharp
using Amazon.Runtime;
using Amazon.S3;
using Headless.Tus;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Models.Expiration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
    new BasicAWSCredentials(builder.Configuration["R2:AccessKeyId"], builder.Configuration["R2:SecretAccessKey"]),
    new AmazonS3Config
    {
        ServiceURL = $"https://{builder.Configuration["R2:AccountId"]}.r2.cloudflarestorage.com",
        ForcePathStyle = true,
        AuthenticationRegion = "auto",
        // R2 rejects the CRC checksums the SDK adds by default.
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    }));

builder.Services.AddTusS3Store(options =>
{
    options.Bucket = "tus-uploads";
    options.Provider = TusS3Provider.R2;
});

builder.Services.AddTusExpiredUploadsCleanup();

var app = builder.Build();

app.MapTus(
    "/files",
    httpContext => Task.FromResult(new DefaultTusConfiguration
    {
        Store = httpContext.RequestServices.GetRequiredService<TusS3Store>(),
        UrlPath = "/files",
        AllowedExtensions = TusExtensions.All,
        // Below R2's 7-day multipart upload window.
        Expiration = new SlidingExpiration(TimeSpan.FromDays(1)),
    }));

app.Run();
```

Amazon S3, with credentials and region from the SDK's default chain (environment, shared profile, instance role):

```csharp
using Amazon.S3;
using Headless.Tus;

builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());

builder.Services.AddTusS3Store(options =>
{
    options.Bucket = "tus-uploads";
    // Provider defaults to TusS3Provider.Aws. Match a bucket abort-incomplete-multipart rule, if one is set:
    options.MultipartUploadLifetime = TimeSpan.FromDays(7);
});
```

MinIO or another S3-compatible server:

```csharp
using Amazon.Runtime;
using Amazon.S3;
using Headless.Tus;

builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
    new BasicAWSCredentials(builder.Configuration["S3:AccessKey"], builder.Configuration["S3:SecretKey"]),
    new AmazonS3Config
    {
        ServiceURL = "http://localhost:9000",
        ForcePathStyle = true,
        AuthenticationRegion = "us-east-1",
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    }));

builder.Services.AddTusS3Store(builder.Configuration.GetSection("Tus"));
```

```json
{
  "Tus": {
    "Bucket": "tus-uploads",
    "Prefix": "uploads/",
    "Provider": "S3Compatible"
  }
}
```

Create the bucket as a deploy step before the host serves uploads:

```bash
dotnet MyApp.dll resources provision -k S3Bucket
```

Custom headers on the finished object:

```csharp
using System.Net.Http.Headers;
using Headless.Tus;

public sealed class DownloadHeadersProvider : ITusS3ObjectHeadersProvider
{
    private readonly DefaultTusS3ObjectHeadersProvider _default = new();

    public async ValueTask<TusS3ObjectHeaders> GetObjectHeadersAsync(Dictionary<string, string> metadata)
    {
        // Keep the default's validated Content-Type from "filetype".
        var headers = await _default.GetObjectHeadersAsync(metadata);

        if (!metadata.TryGetValue("filename", out var fileName) || string.IsNullOrWhiteSpace(fileName))
        {
            return headers;
        }

        // The header type encodes the client-supplied name instead of pasting it into the header raw.
        var disposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName };

        return headers with { ContentDisposition = disposition.ToString() };
    }
}

// Register before AddTusS3Store, which keeps an existing registration.
builder.Services.AddSingleton<ITusS3ObjectHeadersProvider, DownloadHeadersProvider>();
```

Metadata values are client input. Validate or encode them before they become stored response headers, as the default provider does for `filetype`.

### Configuration

`TusS3StoreOptions`:

| Option | Default | Notes |
|---|---|---|
| `Bucket` | `""` | Required. The bucket that holds the uploads. |
| `Prefix` | `"uploads/"` | Key prefix the store owns; must not be empty. A trailing `/` is added when missing. Do not share it with other writers. |
| `Provider` | `TusS3Provider.Aws` | `Aws`, `S3Compatible`, or `R2`; sets the per-request settings and, for `R2`, the default `MultipartUploadLifetime`. |
| `PartSize` | `8 MiB` | Part size for a deferred-length upload, and the minimum for a known one. At least 5 MiB. Sets the deferred-length cap. |
| `MaxPartSize` | `64 MiB` | Largest part size the store picks for a long known-length upload. At least `PartSize`, at most 1 GiB, because each part is buffered in one pooled array. Sets the largest accepted length. |
| `MaxConcurrentPartUploads` | `2` | Parts one PATCH uploads in parallel. At least 1. Bounds memory per PATCH with `MaxPartSize`. |
| `PartClaimWindow` | `64` | Part numbers one state write reserves. 1–250. Four windows are held back from the 10,000-part budget, so a wider window lowers both length limits. |
| `MaxTailSegments` | `16` | Tail segments an upload may hold before the store folds them into one. At least 1. |
| `PersistTimeout` | `30 seconds` | How long the store keeps storing received bytes after a client disconnects. Must be positive; keep it below the distributed lock lease. |
| `MultipartUploadLifetime` | `null` (7 days with `Provider = R2`) | When the provider aborts incomplete multipart uploads. Set it to match a bucket abort rule on AWS or an S3-compatible server. Must be positive when set. |

### Runtime behavior

- Construction makes no network call; the bucket must already exist (see **Bucket provisioning**).
- `AddTusS3Store` registers `TusS3Store` as a singleton, `ITusExpirationStore`, `IHttpContextAccessor`, `TimeProvider.System` (TryAdd), the default `ITusS3ObjectHeadersProvider` (TryAdd), and the `S3Bucket` provisioner. Options run through FluentValidation with `ValidateOnStart`.
- For cross-node PATCH locking, register `Headless.Tus.DistributedLocks`.

---

## Headless.Tus.DistributedLocks

Distributed lock-based TUS file lock provider, using `Headless.DistributedLocks` to prevent concurrent PATCH corruption across multiple application instances.

### API and behavior

- `DistributedLockTusLockProvider` — `ITusFileLockProvider` backed by `IDistributedLock`
- `DistributedLockTusFileLock` — `ITusFileLock` that calls `TryAcquireAsync` with zero wait; returns `false` immediately if another node holds the lock (tusdotnet returns `423 Locked` to the client)
- Lock resource key format: `{resourcePrefix}-{fileId}` with prefix default `tus-file-lock`; give each TUS endpoint its own prefix when several endpoints (different stores/containers) share one `IDistributedLock` backend, so equal file ids cannot contend for the same lock
- Compatible with any `IDistributedLock` backend (Redis, in-memory, etc.)
- Single `AddDistributedLockTusLockProvider(resourcePrefix?)` extension on `IServiceCollection`
- Best-effort mutual exclusion, not fencing: tusdotnet's `ITusFileLock` contract has no hook to observe a lease lost mid-request, so a holder that loses its lease during a backend partition keeps writing until the request ends — the auto-extending lease shrinks that window but cannot eliminate it. The store has to fence: `TusAzureStore` conditions every write on the blob ETag and `TusS3Store` on the state object's ETag, so such a holder's write fails with `412` instead of overwriting the new holder's data

### Install

```bash
dotnet add package Headless.Tus.DistributedLocks
```

### Setup and use

```csharp
using Headless.Tus;
using StackExchange.Redis;
using tusdotnet;
using tusdotnet.Interfaces;
using tusdotnet.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Register a Headless distributed lock backend (Redis shown; any backend works).
//    The Redis backend resolves the IConnectionMultiplexer from DI when the host starts.
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect("localhost:6379"));
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
            Store = tusStore, // your TusAzureStore or TusS3Store instance
            UrlPath = "/files",
            FileLockProvider = lockProvider,
        };
    }
);

app.Run();
```

### Configuration

None beyond registering an `IDistributedLock` provider. The lock acquires with `AcquireTimeout = TimeSpan.Zero` (non-blocking) and `TimeUntilExpires = Timeout.InfiniteTimeSpan` (no expiry while held).

### Runtime behavior

- `AddDistributedLockTusLockProvider()` registers `ITusFileLockProvider` as a singleton (`DistributedLockTusLockProvider`).
- Requires `IDistributedLock` to be registered in DI before this call.
