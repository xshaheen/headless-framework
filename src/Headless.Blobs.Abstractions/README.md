# Headless.Blobs.Abstractions

Defines the unified interfaces for blob/file storage operations across all providers.

## Problem Solved

Application code needs a single, provider-agnostic API for file storage so it can switch between cloud providers, local storage, or test fakes without change. This package defines `IBlobStorage` and the supporting contracts; it carries no implementation and no DI registrations.

## Key Features

- `IBlobStorage` — core interface covering upload, download (`OpenReadStreamAsync`), copy, rename, delete, exists, info, paged listing, bulk upload/delete, and container management.
- `IPresignedUrlBlobStorage` — optional presigned GET + PUT URL capability; implemented only by AWS, Azure, and CloudflareR2.
- `IBlobStorageProvider` — resolves named `IBlobStorage` instances registered through the setup builder (`GetStorage(name)`, `GetStorageOrNull(name)`, `RegisteredNames`).
- `IBlobNamingNormalizer` — provider-specific two-tier path normalization contract.
- Metadata support via `Dictionary<string, string?>`.

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

## Configuration

None. This is an abstractions-only package.

## Dependencies

- `Headless.Extensions`
- `Headless.Serializer.Json`

## Side Effects

None. This is an abstractions package.
