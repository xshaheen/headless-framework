# Headless.Blobs.FileSystem

Local file system implementation of the `IBlobStorage` interface for development and on-premises scenarios.

## Problem Solved

Provides local file system storage using the unified `IBlobStorage` abstraction, ideal for development, testing, and on-premises deployments without cloud dependencies.

## Key Features

- Full `IBlobStorage` implementation using local file system
- Container mapping to directories
- Metadata stored as companion JSON files
- No external service dependencies
- Cross-platform path handling

## Installation

```bash
dotnet add package Headless.Blobs.FileSystem
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFileSystemBlobStorage(options =>
{
    options.BasePath = Path.Combine(builder.Environment.ContentRootPath, "storage");
});
```

## Configuration

### appsettings.json

```json
{
  "FileSystemBlob": {
    "BasePath": "/var/data/blobs"
  }
}
```

### Options

```csharp
options.BasePath = "/path/to/storage";
```

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Hosting`

## Side Effects

- Registers `IBlobStorage` as singleton
- Creates the base directory if it doesn't exist
