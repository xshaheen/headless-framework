# Headless.Blobs.Azure

Azure Blob Storage implementation of `IBlobStorage` for storing files in Azure.

## Problem Solved

Provides integration with Azure Blob Storage using the unified `IBlobStorage` abstraction, with `BlobServiceClient` resolution from DI or a per-store factory, presigned SAS URL support, and opt-in container auto-create.

## Key Features

- Full `IBlobStorage` implementation for Azure Blob Storage.
- Bulk operations with Azure Batch API.
- Opt-in, cached container auto-create (`AutoCreateContainer`, default `true`).
- Metadata support.
- Presigned download/upload URLs via `IPresignedUrlBlobStorage` (SAS-based; named stores only — feature-detect via cast for the default store).
- Per-store `BlobServiceClient` from an optional `clientFactory`; falls back to the ambient `BlobServiceClient` from DI.

## Installation

```bash
dotnet add package Headless.Blobs.Azure
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register a BlobServiceClient in DI (used when no clientFactory is supplied).
builder.Services.AddSingleton(new BlobServiceClient(builder.Configuration["Azure:Storage:ConnectionString"]));

builder.Services.AddHeadlessBlobs(blobs =>
{
    // Default store — consumes the ambient BlobServiceClient from DI.
    blobs.UseAzure(options => { });

    // Named store on a different account — per-store clientFactory overrides the DI client.
    // Also registers keyed IPresignedUrlBlobStorage("archive") automatically.
    blobs.AddNamed(
        "archive",
        instance =>
            instance.UseAzure(
                setupAction: options => { },
                clientFactory: _ => new BlobServiceClient("<archive-connection-string>")
            )
    );
});
```

When no `clientFactory` is supplied, the `BlobServiceClient` must be registered in DI before first use. Absence is detected at resolution time, not at startup.

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

Registered via `AddHeadlessBlobs(b => b.UseAzure(...))` or `AddNamed("name", i => i.UseAzure(...))`:

- Default (`UseAzure`): registers `IBlobStorage` as unkeyed singleton. Consumes `BlobServiceClient` from DI (or `clientFactory`) at resolution time.
- Named (`AddNamed ... UseAzure`): registers `IBlobStorage` as keyed singleton (`name`); registers `IPresignedUrlBlobStorage` as keyed singleton (`name`, forwarded from the keyed `IBlobStorage`).
- Presigned URLs require a `BlobServiceClient` that can sign: account-key clients sign locally; AAD/`DefaultAzureCredential` clients fall back to user-delegation SAS (requires `Storage Blob Delegator` role, capped at 7 days). A bare SAS-token or anonymous client throws `InvalidOperationException` at call time.
