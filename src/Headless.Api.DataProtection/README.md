# Headless.Api.DataProtection

Extends ASP.NET Core Data Protection to persist encryption keys to blob storage providers.

## Problem Solved

In distributed/containerized environments, ASP.NET Core Data Protection keys must be shared across instances. This package enables key persistence to any `IBlobStorage` implementation (Azure, AWS S3, local filesystem, etc.).

## Key Features

- `PersistKeysToBlobStorage()` extension for `IDataProtectionBuilder`
- Works with any `IBlobStorage` implementation
- Ensures the `DataProtection` container before writes when an `IBlobContainerManager` is registered or supplied
- Supports factory-based storage resolution for DI scenarios, including keyed/named stores via a `serviceKey` overload

## Installation

```bash
dotnet add package Headless.Api.DataProtection
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection().PersistKeysToBlobStorage();

// Or with explicit storage instance (pre-provision-only: the DataProtection container is NOT ensured — see Configuration)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(storageInstance);

// Or with explicit storage + container manager (ensures the DataProtection container before writes)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(storageInstance, containerManager);

// Or with factory
builder.Services.AddDataProtection().PersistKeysToBlobStorage(sp => sp.GetRequiredService<IBlobStorage>());

// Or against a named/keyed blob store (resolves the keyed IBlobStorage + IBlobContainerManager)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(serviceKey: "keys");
```

## Configuration

No specific configuration. Depends on the underlying `IBlobStorage` configuration. Cloud/object-store providers should also register or pass the matching `IBlobContainerManager` so the `DataProtection` container is created before the first key write.

The storage-only `PersistKeysToBlobStorage(storage)` overload is **pre-provision-only**: it never ensures the container, and the blob data plane treats a missing container as an error (not auto-created), so on a fresh deployment the first key write fails unless the `DataProtection` container already exists. Provision it via `IBlobContainerManager.EnsureContainerAsync` at startup or out-of-band (portal, CLI, IaC), or prefer the `(storage, containerManager)` overload. Cloudflare R2 is the provider where pre-provisioning is the only option — it deliberately ships no `IBlobContainerManager` (R2 object-scoped tokens cannot create buckets), so create the bucket in the Cloudflare dashboard/API first.

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Checks`
- `Azure.Extensions.AspNetCore.DataProtection.Blobs`
- `Microsoft.AspNetCore.DataProtection`

## Side Effects

- Configures `KeyManagementOptions.XmlRepository` to use blob storage
