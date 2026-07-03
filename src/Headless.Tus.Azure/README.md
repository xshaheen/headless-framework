# Headless.Tus.Azure

Azure Blob Storage TUS store implementation.

## Problem Solved

Provides `TusAzureStore`, a complete `ITusStore` implementation that backs resumable uploads with Azure Blob Storage block blobs. Supports all major TUS extensions: Creation, CreationDeferLength, Concatenation, Expiration, Checksum, and Termination.

## Key Features

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
- `AddTusAzureStore` — optional DI registration (overloads for `IConfiguration`, `Action<TOptions>`, `Action<TOptions, IServiceProvider>`) with FluentValidation + `ValidateOnStart`; manual construction stays fully supported
- Adaptive chunk sizing: automatic selection between `BlobDefaultChunkSize` (4 MB) and `BlobMaxChunkSize` (16 MB default; up to 100 MB) based on declared upload size
- Pooled buffer stream splitting via `ArrayPool<byte>` to minimize allocations during PATCH ingestion

## Design Notes

**Block blob chunking and checksum deferral.** When the client sends `Upload-Checksum` as a request _header_, `TusAzureStore` stages blocks during `AppendDataAsync` but defers the commit until `VerifyChecksumAsync` succeeds. The staged block range — a constant-size `token:firstIndex:count` triple that reconstructs the exact block IDs at commit time — and the pre-calculated checksum are written to blob metadata (`tus_last_chunk_blocks`, `tus_last_chunk_checksum`); being constant-size, the tracking cannot approach Azure's 8 KB blob-metadata cap no matter how many blocks one PATCH stages. On success, blocks are committed atomically together with metadata update. On mismatch, blocks are left uncommitted and Azure auto-purges them within 7 days. This means `GetUploadOffsetAsync` reads committed block sizes — not the blob `ContentLength` property — to report accurate offset to resuming clients.

**Checksum trailers.** When `Upload-Checksum` arrives as an HTTP _trailer_, the digest is unknown while the body streams, so `AppendDataAsync` commits immediately and records the pre-append offset (`tus_last_chunk_offset`) as a rollback point. `VerifyChecksumAsync` then hashes the committed `[offset, end)` range on demand; on mismatch — or when tusdotnet signals a faulty/missing trailer via its fallback sentinel — the blob is rolled back by re-committing the previous block list (the Azure analog of `TusDiskStore`'s `SetLength`). Verification and rollback deliberately ignore the request's cancellation token: it is already cancelled when the client disconnected mid-trailer, and the cleanup must complete anyway.

**Cancellation policy (TusDiskStore parity).** Client reads use the request token — it is the only disconnect signal — while everything that persists already-received bytes runs on `CancellationToken.None`: block staging, block-list commits, chunk-tracking metadata, sliding-expiration refresh, checksum verification/rollback, and expired-file deletion once a file is enumerated. A disconnect therefore commits the bytes received so far (the tus spec: the server "SHOULD always attempt to store as much of the received data as possible") and the client resumes from them instead of re-uploading. One exception: the `EnableChunkSplitting=false` fast path for seekable bodies streams directly into `StageBlock` with the live token — tusdotnet request bodies are never seekable, so that path is store-direct only. One deliberate divergence: a checksum PATCH whose chunk was fully received and hashed just before the disconnect is *committed* (its digest verifies), whereas `TusDiskStore` discards a chunk whenever the client disconnected mid-request — keeping verified bytes serves the spec better, and the resuming client sees the advanced offset on HEAD.

**Expiration reporting.** `GetExpirationAsync` returns `null` for completed uploads — a deliberate divergence from `TusDiskStore`. tusdotnet refreshes the sliding expiration on the completing PATCH and its `FileHasNotExpired` requirement 404s any upload whose expiration passed, so reporting the stale timestamp would make completed uploads unreachable through tus (no HEAD, and no DELETE/termination) even though this store never reaps them. Cleanup (`GetExpiredFilesAsync`/`RemoveExpiredFilesAsync`) likewise targets incomplete uploads only.

**Server-side concatenation.** Final-file creation uses `StageBlockFromUri` to copy block ranges across blobs without moving data through the application server. When server-side copy is unavailable — Azurite returns HTTP 501, and a private container returns HTTP 403 because the source blob is not readable via its bare URI (no SAS) — the store falls back to download-then-re-upload streaming automatically.

**Constructor-time container init.** When `CreateContainerIfNotExists = true`, `_containerClient.CreateIfNotExists(ContainerPublicAccessType)` is called synchronously in the constructor. If the `BlobServiceClient` lacks container-create permission, construction fails.

**Blob HTTP header preservation.** Headers returned by `ITusAzureBlobHttpHeadersProvider` are applied at creation and re-supplied on every block-list commit — appends, checksum-verified commits, and rollbacks. Azure's Put Block List clears any `x-ms-blob-*` property omitted from the request, so without the re-supply a custom content type or cache control set at creation would silently reset to `application/octet-stream` on the first PATCH. `ContentHash` is deliberately not carried over: the content changes with each commit, so echoing the stored MD5 would persist a stale digest.

**Limits & input validation.** An upload is capped at Azure's 50,000 committed blocks; exceeding it (a too-small chunk size on a very large upload) throws a `TusStoreException` at commit time — raise `BlobDefaultChunkSize`/`BlobMaxChunkSize` for fewer, larger blocks. File ids become blob names, so ids from a custom `ITusFileIdProvider` must not contain `/`, `\`, `..`, `,` (the separator used to persist file-id lists in blob metadata), control characters, or leading/trailing whitespace — the store rejects such ids with a `TusStoreException` at every entry point in addition to enforcing the provider's own `ValidateId`.

**Verbatim Upload-Metadata storage.** The client's `Upload-Metadata` header value is stored untouched in a single blob-metadata entry (`tus_metadata`), so HEAD responses echo it byte-for-byte as the TUS spec requires — keys keep their casing and characters, and values may decode to any UTF-8 content (the raw string itself is ASCII because values are base64). The decoded view (original keys, UTF-8 values) is what `ITusAzureBlobHttpHeadersProvider` receives. Upload-Metadata is rejected with a `TusStoreException` when it is not printable ASCII or exceeds ~7 KB (Azure caps total blob metadata at 8 KB). A concatenation _final_ additionally persists `tus_partial_uploads` (the comma-joined constituent ids) alongside the verbatim metadata, so `CreateFinalFileAsync` re-checks the composed total against the 8 KB cap before committing and throws an actionable `TusStoreException` instead of an opaque Azure 400 when a final with many partials plus near-cap metadata would overflow it.

## Installation

```bash
dotnet add package Headless.Tus.Azure
```

## Quick Start

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

Serializing concurrent PATCHes across nodes (multi-node deployments): register
[`Headless.Tus.DistributedLocks`](../Headless.Tus.DistributedLocks/README.md), which bridges any
`IDistributedLock` backend (Redis, SQL Server, …) to tusdotnet's `ITusFileLockProvider`:

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

A single-node deployment can omit the lock provider entirely and rely on tusdotnet's in-process lock.

Prefer DI? `AddTusAzureStore` registers the store as a singleton with validated options; it consumes the
`BlobServiceClient` registered by the app (for example via `Microsoft.Extensions.Azure`'s `AddAzureClients`):

```csharp
builder.Services.AddAzureClients(azure => azure.AddBlobServiceClient(connectionString));
builder.Services.AddTusAzureStore(builder.Configuration.GetSection("Tus:Azure"));

app.MapTus(
    "/files",
    httpContext => Task.FromResult(new DefaultTusConfiguration
    {
        Store = httpContext.RequestServices.GetRequiredService<TusAzureStore>(),
        UrlPath = "/files",
    })
);
```

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

## Configuration

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
| `MaxNoSplitBufferSize` | `256 MB` | In-memory buffer cap for a single PATCH body when `EnableChunkSplitting` is `false`; a body exceeding it is rejected with a `TusStoreException`. It is the only in-flight memory bound for Creation-Defer-Length no-split uploads (declared length unknown, so the too-much-data guard cannot apply). Ignored when splitting is enabled. Must be 1 byte–~2 GB. |
| `DeletePartialFilesOnConcat` | `false` | Delete the partial uploads after a final upload is committed. Keep `false` if clients reuse partials across finals (the spec allows it). Deletion is best-effort and never fails the request. |

Chunk size selection: uploads < 10 MB use `min(BlobDefaultChunkSize, fileSize)`; uploads 10–100 MB use `BlobDefaultChunkSize`; uploads ≥ 100 MB use `BlobMaxChunkSize`.

## Dependencies

- `Headless.Tus`
- `Azure.Storage.Blobs`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

- Synchronously calls `BlobContainerClient.CreateIfNotExists` during `TusAzureStore` construction when `CreateContainerIfNotExists = true`.
- DI registration is optional — tusdotnet composes stores inside `DefaultTusConfiguration` factories, so `TusAzureStore` can be constructed manually; `AddTusAzureStore` additionally offers options-pipeline registration (requires a `BlobServiceClient` in DI; the container is still created synchronously at first resolution when enabled). For cross-node PATCH locking, register `Headless.Tus.DistributedLocks`.
