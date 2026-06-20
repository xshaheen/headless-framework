# Headless.Blobs.Azure

Azure Blob Storage implementation of the `IBlobStorage` interface for storing files in Azure.

## Problem Solved

Provides seamless integration with Azure Blob Storage using the unified `IBlobStorage` abstraction.

## Key Features

- Full `IBlobStorage` implementation for Azure Blob Storage
- Bulk operations with Azure Batch API
- Opt-in, cached container auto-create (`AutoCreateContainer`)
- Metadata support
- Presigned download/upload URLs via `IPresignedUrlBlobStorage` (SAS-based)
- Integration with Azure.Identity for authentication

> Presigned URLs require the registered `BlobServiceClient` to carry signing credentials (an account key or a
> user-delegation key). A client built from a bare SAS token or an anonymous connection cannot sign, and the
> presigned call throws `InvalidOperationException` at call time.

> `AutoCreateContainer` (default `true`) creates the target container on upload/copy, at most once per
> container per instance. Set it to `false` when the client's credentials cannot create containers; a missing
> container then surfaces as an error. (Renamed from `CreateContainerIfNotExists`.)

## Installation

```bash
dotnet add package Headless.Blobs.Azure
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register a BlobServiceClient (from a connection string, or via Microsoft.Extensions.Azure with
// DefaultAzureCredential). The Azure store consumes it from DI.
builder.Services.AddSingleton(new BlobServiceClient(builder.Configuration["Azure:Storage:ConnectionString"]));
builder.Services.AddHeadlessBlobs(blobs => blobs.UseAzure(options => { }));

// For a named store on a different account, supply a per-store client:
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.AddNamed("archive", instance => instance.UseAzure(
        setupAction: options => { },
        clientFactory: _ => new BlobServiceClient("<archive-account-connection-string>"))));
```

## Configuration

### appsettings.json

```json
{
  "Azure": {
    "Storage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
      "ContainerName": "my-container"
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

- Registers `BlobServiceClient` via Azure client factory
- Registers `IBlobStorage` as singleton
- Registers `IBlobNamingNormalizer` as singleton
