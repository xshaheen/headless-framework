# Headless.Tus.Azure

Azure Blob Storage TUS store implementation.

## Problem Solved

Provides a full-featured TUS protocol store using Azure Blob Storage for resumable file uploads, supporting creation, concatenation, expiration, checksum verification, and termination extensions.

## Key Features

- `TusAzureStore` - Complete ITusStore implementation
- Supports TUS extensions:
  - Creation / CreationDeferLength
  - Concatenation
  - Expiration
  - Checksum
  - Termination
- Azure Blob file locking
- Configurable blob prefix and container
- Automatic container creation

## Installation

```bash
dotnet add package Headless.Tus.Azure
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var blobServiceClient = new BlobServiceClient(connectionString);

var store = new TusAzureStore(
    blobServiceClient,
    new TusAzureStoreOptions
    {
        ContainerName = "uploads",
        BlobPrefix = "tus/",
        CreateContainerIfNotExists = true
    }
);

var app = builder.Build();

app.MapTus("/files", async _ => new DefaultTusConfiguration
{
    Store = store,
    UrlPath = "/files"
});
```

## Configuration

```csharp
var options = new TusAzureStoreOptions
{
    ContainerName = "uploads",
    BlobPrefix = "tus/",
    CreateContainerIfNotExists = true,
    ContainerPublicAccessType = PublicAccessType.None
};
```

## Dependencies

- `Headless.Tus`
- `Azure.Storage.Blobs`
- `tusdotnet`

## Side Effects

- Creates Azure Blob container if configured
