# Headless.Blobs.Azure

Azure Blob Storage implementation of the `IBlobStorage` interface for storing files in Azure.

## Problem Solved

Provides Azure Blob Storage integration using the unified `IBlobStorage` abstraction.

## Key Features

- Full `IBlobStorage` implementation for Azure Blob Storage
- Bulk operations with Azure Batch API
- Opt-in, cached container auto-create (`AutoCreateContainer`)
- Metadata support
- Presigned download/upload URLs via `IPresignedUrlBlobStorage` (SAS-based)
- Integration with Azure.Identity for authentication

> Presigned URLs need a `BlobServiceClient` that can sign. Account-key clients sign locally; AAD /
> `DefaultAzureCredential` clients fall back to a user-delegation SAS (the identity needs the `Storage Blob
> Delegator` role plus a data role, and the URL is capped at 7 days). A bare SAS-token or anonymous client
> cannot sign and throws `InvalidOperationException` at call time.

> `AutoCreateContainer` (default `true`) creates the target container on upload/copy, at most once per
> container per instance. Set it to `false` when the client's credentials cannot create containers; a missing
> container then surfaces as an error. (Renamed from `CreateContainerIfNotExists`.)

## Installation

```bash
dotnet add package Headless.Blobs.Azure
```

## Quick Start

Register a `BlobServiceClient` in DI first, then call `AddAzureBlobStorage`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: connection string
builder.Services.AddSingleton(new BlobServiceClient(
    builder.Configuration["Azure:Storage:ConnectionString"]));

// Option 2: Azure.Identity (DefaultAzureCredential)
builder.Services.AddSingleton(new BlobServiceClient(
    new Uri($"https://mystorageaccount.blob.core.windows.net"),
    new DefaultAzureCredential()));

// Option 3: Aspire integration
// builder.AddAzureBlobClient("blobs");

builder.Services.AddAzureBlobStorage(options =>
{
    options.AutoCreateContainer = true; // default
});
```

## Configuration

### appsettings.json

```json
{
  "Azure": {
    "Storage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
    }
  }
}
```

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `Azure.Storage.Blobs`
- `Azure.Storage.Blobs.Batch`
- `Microsoft.Extensions.Azure`

## Side Effects

- Requires a `BlobServiceClient` to be registered in DI before this call
- Registers `IBlobStorage` as singleton
- Registers `IBlobNamingNormalizer` as singleton
