# Headless.Blobs.FileSystem

Local file system implementation of `IBlobStorage` for development and on-premises scenarios.

## Problem Solved

Provides local file system storage using the unified `IBlobStorage` abstraction, for development, testing, and on-premises deployments without cloud dependencies.

## Key Features

- Full `IBlobStorage` implementation using the local file system, routed through the shared resolve seam.
- Container mapping to directories under a configured base path.
- Metadata stored in a sidecar companion file (reserved `.hlmeta` suffix) beside each blob.
- Emulated paging: `ListAsync` enumerates sorted by key and encodes a lexicographic start-after-key as the opaque token.
- Container lifecycle via `FileSystemBlobContainerManager` resolved from DI (`EnsureContainerAsync` creates the root directory).
- No external service dependencies; cross-platform path handling.

## Design Notes

- **Universal metadata via sidecars.** The file system has no native blob-metadata concept, so metadata is persisted in a companion `.hlmeta` file written content-first after the blob. A missing sidecar reads as empty metadata, so reads stay safe across a crash window, but the pair is **non-atomic**. Sidecars are filtered from every listing/exists/count/delete result and never surface as blobs; a blob key ending in `.hlmeta` is rejected at `BlobLocation` construction. Deleting or moving a blob deletes/moves its sidecar, so re-uploading the same key without metadata returns no stale metadata.
- **Emulated paging tier.** `ListAsync` re-scans the directory sorted by key and resumes after the start-after-key token. The token is serializable and survives a request boundary, but stability is weaker than S3/Azure under concurrent writes (a blob inserted before the resume point can be skipped or repeated) — the same cost profile as the previous implementation.
- **Path-dir creation, not container creation.** `UploadAsync` creates the intermediate directories needed to write the blob, but a missing top-level container is still an error unless created via `EnsureContainerAsync`. Non-seekable upload streams are written straight to disk (no buffering).

## Installation

```bash
dotnet add package Headless.Blobs.FileSystem
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseFileSystem(options =>
        options.BaseDirectoryPath = Path.Combine(builder.Environment.ContentRootPath, "storage")
    )
);
```

## Configuration

### appsettings.json

```json
{
  "FileSystemBlob": {
    "BaseDirectoryPath": "/var/data/blobs"
  }
}
```

### Options

```csharp
options.BaseDirectoryPath = "/path/to/storage"; // required; the root directory for all containers
```

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Blobs.Core`
- `Headless.Core`
- `Headless.Hosting`

## Side Effects

Registered via `AddHeadlessBlobs(b => b.UseFileSystem(...))` or `AddNamed("name", i => i.UseFileSystem(...))`:

- Default (`UseFileSystem`): registers `IBlobStorage` as unkeyed singleton and `IBlobContainerManager` as unkeyed singleton (`FileSystemBlobContainerManager`).
- Named (`AddNamed ... UseFileSystem`): registers `IBlobStorage` and `IBlobContainerManager` each as keyed singleton (`name`).
- No presigned URL support — `IPresignedUrlBlobStorage` is never registered for FileSystem stores.
