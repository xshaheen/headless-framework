# Headless.Blobs.Abstractions

Defines the unified interfaces and value types for blob/file storage operations across all providers.

## Problem Solved

Application code needs a single, provider-agnostic API for file storage so it can switch between cloud providers, local storage, or test fakes without change. This package defines `IBlobStorage`, the `BlobLocation` address type, and the supporting contracts; it carries no implementation and no DI registrations.

## Key Features

- `IBlobStorage` — data-plane interface covering upload, download (`OpenReadStreamAsync`), copy, move (non-atomic), delete, exists, info, token-based listing (`ListAsync`), and bulk upload/delete.
- `BlobLocation` — validated `(Container, Path)` address value type; constructor enforces path security and offers a `params string[]` segment overload.
- `BlobQuery` / `BlobPage` — token-based paging primitive: a prefix-scoped page request and its result plus an opaque continuation token.
- `BlobBulkResult` — identity-carrying bulk outcome (`Container` + `Path` + optional validated `BlobLocation` + `Result<bool, Exception>`).
- `IBlobContainerManager` — optional container-lifecycle capability (Ensure/Exists/Delete), resolved from DI; implemented by AWS, Azure, FileSystem, Redis, and SSH (not R2).
- `IPresignedUrlBlobStorage` — optional presigned GET + PUT URL capability over a `BlobLocation`; implemented only by AWS, Azure, and CloudflareR2.
- `IBlobStorageProvider` — resolves named `IBlobStorage` instances registered through the setup builder (`GetStorage(name)`, `GetStorageOrNull(name)`, `RegisteredNames`).
- `IBlobNamingNormalizer` — provider-specific two-tier path normalization contract applied by the provider's resolve step.
- `BlobStorageExtensions` — `GetBlobsAsync` streaming + glob filter, `GetBlobsListAsync` materializer, and `UploadContentAsync`/`GetBlobContentAsync` (text + JSON) convenience helpers.
- Consistent metadata typing: `IReadOnlyDictionary<string, string>?` with non-null values.

## Installation

```bash
dotnet add package Headless.Blobs.Abstractions
```

## Quick Start

```csharp
public sealed class FileService(IBlobStorage storage)
{
    public async Task UploadAsync(Stream file, string fileName, CancellationToken ct)
    {
        var location = new BlobLocation("uploads", "images", fileName); // container "uploads", key "images/<fileName>"

        await storage.UploadAsync(
            location,
            file,
            metadata: new Dictionary<string, string> { ["uploaded-by"] = "user-123" },
            cancellationToken: ct
        );
    }

    public async Task<string?> GetContentAsync(string fileName, CancellationToken ct)
    {
        var location = new BlobLocation("uploads", "images", fileName);

        // Dispose result promptly — holding it may exhaust connection pools.
        await using var result = await storage.OpenReadStreamAsync(location, ct);
        if (result is null)
            return null;

        using var reader = new StreamReader(result.Stream);
        return await reader.ReadToEndAsync(ct);
    }

    public async Task<IReadOnlyList<BlobInfo>> ListImagesAsync(CancellationToken ct)
    {
        // Stream every page transparently; Prefix is pushed to the backend.
        var query = new BlobQuery("uploads", prefix: "images/");
        var blobs = new List<BlobInfo>();
        await foreach (var blob in storage.GetBlobsAsync(query, ct))
            blobs.Add(blob);
        return blobs;
    }
}
```

Resolve a named store, manage a container, or check for presigned support:

```csharp
public sealed class StorageService(
    IBlobStorageProvider provider,
    [FromKeyedServices("archive")] IBlobStorage archiveStorage
)
{
    // Validate an externally-supplied name before resolving:
    public bool IsKnownStore(string name) => provider.RegisteredNames.Contains(name);

    // Resolve by name:
    public IBlobStorage GetByName(string name) => provider.GetStorage(name); // throws if not found

    public IBlobStorage? TryGetByName(string name) => provider.GetStorageOrNull(name); // null if not found

    // Container management is resolved from DI, not cast from the store (null for R2):
    public async Task EnsureAsync(IServiceProvider sp, string container, CancellationToken ct)
    {
        var manager = sp.GetService<IBlobContainerManager>();
        if (manager is not null)
            await manager.EnsureContainerAsync(container, ct);
    }

    // Feature-detect presigned on the default store (is-cast is honest here):
    public async Task<Uri?> TryGetPresignedUrlAsync(IBlobStorage storage, BlobLocation location)
    {
        if (storage is not IPresignedUrlBlobStorage presigned)
            return null;
        return await presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.FromMinutes(15));
    }
}
```

## Configuration

None. This is an abstractions-only package.

## Dependencies

- `Headless.Extensions`
- `Headless.Serializer.Json`

## Side Effects

None. This is an abstractions package.
