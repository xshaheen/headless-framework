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
- `AzureBlobFileLockProvider` / `AzureBlobFileLock` — Azure Blob Lease-based `ITusFileLockProvider` for single-region deployments
- `ITusAzureBlobHttpHeadersProvider` / `DefaultTusAzureBlobHttpHeadersProvider` — per-file HTTP header customization (Content-Type, cache control)
- Adaptive chunk sizing: automatic selection between `BlobDefaultChunkSize` (4 MB) and `BlobMaxChunkSize` (100 MB) based on declared upload size
- Pooled buffer stream splitting via `ArrayPool<byte>` to minimize allocations during PATCH ingestion

## Design Notes

**Block blob chunking and checksum deferral.** TUS Checksum verification happens _after_ all PATCH data is staged. `TusAzureStore` stages blocks during `AppendDataAsync` but defers the commit list until `VerifyChecksumAsync` succeeds. The staged block IDs and pre-calculated checksum are written to blob metadata (`tus_last_chunk_blocks`, `tus_last_chunk_checksum`). On success, blocks are committed atomically together with metadata update. On mismatch, blocks are left uncommitted and Azure auto-purges them within 7 days. This means `GetUploadOffsetAsync` reads committed block sizes — not the blob `ContentLength` property — to report accurate offset to resuming clients.

**Server-side concatenation.** Final-file creation uses `StageBlockFromUri` to copy block ranges across blobs without moving data through the application server. Azurite returns HTTP 501 for this API; the store detects this and falls back to download-then-re-upload streaming automatically.

**Constructor-time container init.** When `CreateContainerIfNotExists = true`, `_containerClient.CreateIfNotExists(ContainerPublicAccessType)` is called synchronously in the constructor. If the `BlobServiceClient` lacks container-create permission, construction fails.

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

With Azure Blob file locking (single-region):

```csharp
using Headless.Tus.Locks;
using Headless.Tus.Options;
using tusdotnet.Interfaces;

var options = new TusAzureStoreOptions
{
    ContainerName = "uploads",
    BlobPrefix = "tus/",
    LeaseDuration = Timeout.InfiniteTimeSpan   // infinite or 15 s–60 min
};

var lockProvider = new AzureBlobFileLockProvider(blobServiceClient, options);

app.MapTus("/files", async _ => new DefaultTusConfiguration
{
    Store = tusStore,
    UrlPath = "/files",
    FileLockProvider = lockProvider
});
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
| `BlobDefaultChunkSize` | `4 MB` | Default block size for medium uploads. |
| `BlobMaxChunkSize` | `100 MB` | Block size used for uploads ≥ 100 MB. |
| `LeaseDuration` | `Timeout.InfiniteTimeSpan` | Used by `AzureBlobFileLockProvider`. Must be infinite or 15 s–60 min. |

Chunk size selection: uploads < 10 MB use `min(BlobDefaultChunkSize, fileSize)`; uploads 10–100 MB use `BlobDefaultChunkSize`; uploads ≥ 100 MB use `BlobMaxChunkSize`.

## Dependencies

- `Headless.Tus`
- `Azure.Storage.Blobs`
- `Azure.Storage.Blobs.Batch`
- `Microsoft.Extensions.Azure`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

- Synchronously calls `BlobContainerClient.CreateIfNotExists` during `TusAzureStore` construction when `CreateContainerIfNotExists = true`.
- No DI registrations — `TusAzureStore` and `AzureBlobFileLockProvider` are constructed manually.
