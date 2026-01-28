# Headless.Blobs.Azure

Azure Blob Storage implementation of the `IBlobStorage` interface for storing files in Azure.

## Problem Solved

Provides seamless integration with Azure Blob Storage using the unified `IBlobStorage` abstraction.

## Key Features

- Full `IBlobStorage` implementation for Azure Blob Storage
- Bulk operations with Azure Batch API
- Container management
- Metadata support
- Integration with Azure.Identity for authentication

## Installation

```bash
dotnet add package Headless.Blobs.Azure
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAzureBlobStorage(options =>
{
    options.ConnectionString = builder.Configuration["Azure:Storage:ConnectionString"];
    options.ContainerName = "my-container";
});

// Or with Azure.Identity
builder.Services.AddAzureBlobStorage(options =>
{
    options.AccountName = "mystorageaccount";
    options.ContainerName = "my-container";
    // Uses DefaultAzureCredential
});
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
- `Headless.BuildingBlocks`
- `Headless.Hosting`
- `Azure.Storage.Blobs`
- `Azure.Storage.Blobs.Batch`
- `Microsoft.Extensions.Azure`

## Side Effects

- Registers `BlobServiceClient` via Azure client factory
- Registers `IBlobStorage` as singleton
- Registers `IBlobNamingNormalizer` as singleton
