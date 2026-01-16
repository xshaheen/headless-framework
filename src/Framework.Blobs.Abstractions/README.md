# Framework.Blobs.Abstractions

Defines the unified interface for blob/file storage operations across different providers (AWS S3, Azure Blob, FileSystem, Redis, SFTP).

## Problem Solved

Provides a provider-agnostic API for file storage operations, enabling seamless switching between cloud providers or local storage without changing application code.

## Key Features

- `IBlobStorage` - Core interface for all storage operations:
  - Upload blobs with metadata
  - Open read stream for downloading
  - Bulk upload/delete operations
  - Copy/Rename/Delete operations
  - Exists check and blob info retrieval
  - Paged listing with search patterns
- `IBlobNamingNormalizer` - Provider-specific path normalization
- Container/directory management
- Metadata support

## Installation

```bash
dotnet add package Framework.Blobs.Abstractions
```

## Usage

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
        // IMPORTANT: Dispose result promptly - holding it may exhaust connection pools
        await using var result = await storage.OpenReadStreamAsync(["uploads", "images"], fileName, ct);
        if (result is null) return null;

        using var reader = new StreamReader(result.Stream);
        return await reader.ReadToEndAsync(ct);
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

- `Framework.Base`
- `Framework.Serializer.Json`

## Side Effects

None. This is an abstractions package.
