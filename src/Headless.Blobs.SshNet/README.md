# Headless.Blobs.SshNet

SFTP/SSH implementation of `IBlobStorage` for storing files on remote servers via SFTP.

## Problem Solved

Provides blob storage via SFTP/SSH for scenarios requiring file transfer to remote servers, legacy system integration, or secure file exchange with systems that do not expose a cloud API.

## Key Features

- Full `IBlobStorage` implementation using SFTP, routed through the shared resolve seam.
- SSH key and password authentication.
- Metadata stored in a sidecar companion file (reserved `.hlmeta` suffix) beside each blob.
- Emulated paging: `ListAsync` lists recursively, sorts by key, and encodes a start-after-key as the opaque token.
- Container lifecycle via `SshBlobContainerManager` resolved from DI (`EnsureContainerAsync` is a validated `mkdir -p`).
- Connection pooling via an internal SFTP client pool; each store owns its own pool.

## Design Notes

- **Universal metadata via sidecars.** SFTP has no native blob-metadata concept, so metadata is persisted in a companion `.hlmeta` file written content-first after the blob — a second round-trip per metadata-bearing write/read. A missing sidecar reads as empty metadata; the pair is **non-atomic**. Sidecars are filtered from every listing/exists/count/delete result; a blob key ending in `.hlmeta` is rejected at `BlobLocation` construction. Deleting or moving a blob deletes/moves its sidecar.
- **Non-atomic `Move`.** `MoveAsync` is copy-then-delete with best-effort destination rollback; the sidecar moves with the blob. There is no atomic server-side rename.
- **Emulated paging tier + validated directory creation.** `ListAsync` re-scans recursively, sorted by key, resuming after the start-after-key token (weaker stability under concurrent writes). `EnsureContainerAsync` and the upload/move retry paths validate and normalize every path segment through the resolve seam, so created directories match where uploads are written. Non-seekable upload streams pass through to the SFTP write stream unbuffered.

## Consumer Responsibilities

Like cache-key length and message payload sizes elsewhere in the framework, connection-pool lifetime is delegated to the consumer — the pool applies backpressure but does not police caller mistakes:

- **Dispose every `OpenReadStreamAsync` result promptly** (`await using`, or a `finally`). The returned `BlobDownloadResult` owns a pooled `SftpClient` until it is disposed; an undisposed result **leaks a pool slot**. After `MaxPoolSize` leaks the pool is exhausted and every subsequent operation blocks. `OpenReadStreamAsync` is annotated `[MustDisposeResource]` so the analyzer flags a missed dispose.
- **The internal SFTP connection pool has no acquire timeout — this is deliberate backpressure.** When all `MaxPoolSize` connections are busy, acquiring blocks until a slot frees or the operation's `CancellationToken` is cancelled. Size `MaxPoolSize` for your peak concurrency and always pass a cancellation token so a saturated pool fails fast instead of hanging.

## Installation

```bash
dotnet add package Headless.Blobs.SshNet
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseSsh(options => options.ConnectionString = "sftp://user:password@sftp.example.com:22/home/user/uploads")
);
```

## Configuration

### appsettings.json

```json
{
  "SftpBlob": {
    "ConnectionString": "sftp://user:password@sftp.example.com:22/home/user/uploads"
  }
}
```

### SSH Key Authentication

```csharp
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseSsh(options =>
    {
        options.ConnectionString = "sftp://user@sftp.example.com:22/home/user/uploads";
        options.PrivateKey = File.OpenRead("/path/to/key");
        options.PrivateKeyPassPhrase = "optional-passphrase"; // nullable
    })
);
```

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Blobs.Core`
- `Headless.Hosting`
- `Headless.Serializer.Json`
- `SSH.NET`

## Side Effects

Registered via `AddHeadlessBlobs(b => b.UseSsh(...))` or `AddNamed("name", i => i.UseSsh(...))`:

- Default (`UseSsh`): registers an internal SFTP connection pool as unkeyed singleton; registers `IBlobStorage` as unkeyed singleton and `IBlobContainerManager` as unkeyed singleton (internal `SshBlobContainerManager`).
- Named (`AddNamed ... UseSsh`): registers the internal SFTP connection pool, `IBlobStorage`, and `IBlobContainerManager` each as keyed singleton (`name`). Each named store owns its own pool instance bound to its named options.
- No presigned URL support — `IPresignedUrlBlobStorage` is never registered for SshNet stores.
