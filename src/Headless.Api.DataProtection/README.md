# Headless.Api.DataProtection

Extends ASP.NET Core Data Protection to persist encryption keys to blob storage providers.

## Problem Solved

In distributed/containerized environments, ASP.NET Core Data Protection keys must be shared across instances. This package enables key persistence to any `IBlobStorage` implementation (Azure, AWS S3, local filesystem, etc.).

## Key Features

- `PersistKeysToBlobStorage()` extension for `IDataProtectionBuilder`
- Works with any `IBlobStorage` implementation
- Supports factory-based storage resolution for DI scenarios

## Installation

```bash
dotnet add package Headless.Api.DataProtection
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection()
    .PersistKeysToBlobStorage();

// Or with explicit storage instance
builder.Services.AddDataProtection()
    .PersistKeysToBlobStorage(storageInstance);

// Or with factory
builder.Services.AddDataProtection()
    .PersistKeysToBlobStorage(sp => sp.GetRequiredService<IBlobStorage>());
```

## Configuration

No specific configuration. Depends on the underlying `IBlobStorage` configuration.

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Checks`
- `Azure.Extensions.AspNetCore.DataProtection.Blobs`
- `Microsoft.AspNetCore.DataProtection`

## Side Effects

- Configures `KeyManagementOptions.XmlRepository` to use blob storage
