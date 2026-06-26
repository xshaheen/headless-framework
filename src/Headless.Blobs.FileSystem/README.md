# Headless.Blobs.FileSystem

Local file system implementation of `IBlobStorage` for development and on-premises scenarios.

## Problem Solved

Provides local file system storage using the unified `IBlobStorage` abstraction, for development, testing, and on-premises deployments without cloud dependencies.

## Key Features

- Full `IBlobStorage` implementation using local file system.
- Container mapping to directories.
- Metadata stored as companion JSON files.
- No external service dependencies.
- Cross-platform path handling.

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
- `Headless.Hosting`

## Side Effects

Registered via `AddHeadlessBlobs(b => b.UseFileSystem(...))` or `AddNamed("name", i => i.UseFileSystem(...))`:

- Default (`UseFileSystem`): registers `IBlobStorage` as unkeyed singleton.
- Named (`AddNamed ... UseFileSystem`): registers `IBlobStorage` as keyed singleton (`name`).
- No presigned URL support — `IPresignedUrlBlobStorage` is never registered for FileSystem stores.
