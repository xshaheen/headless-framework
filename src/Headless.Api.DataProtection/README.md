# Headless.Api.DataProtection

Extends ASP.NET Core Data Protection to persist encryption keys to blob storage providers.

## Problem Solved

In distributed/containerized environments, ASP.NET Core Data Protection keys must be shared across instances. This package enables key persistence to any `IBlobStorage` implementation (Azure, AWS S3, local filesystem, etc.).

## Key Features

- `PersistKeysToBlobStorage()` extension for `IDataProtectionBuilder`
- Works with any `IBlobStorage` implementation
- Ensures the `DataProtection` container before writes when an `IBlobContainerManager` is registered or supplied
- Supports factory-based storage resolution for DI scenarios, including keyed/named stores via a `serviceKey` overload
- Enforces container provisioning up front: when no manager is available and the storage requires a provisioned container (`IBlobStorage.RequiresContainerProvisioning`), configuration throws unless you acknowledge out-of-band provisioning with `provisioning: BlobContainerProvisioning.PreProvisioned`

## Installation

```bash
dotnet add package Headless.Api.DataProtection
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection().PersistKeysToBlobStorage();

// Or with explicit storage instance (no manager: throws at config time for provisioning-requiring backends
// unless you acknowledge out-of-band provisioning — see Configuration)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(storageInstance, provisioning: BlobContainerProvisioning.PreProvisioned);

// Or with explicit storage + container manager (ensures the DataProtection container before writes)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(storageInstance, containerManager);

// Or with factory
builder.Services.AddDataProtection().PersistKeysToBlobStorage(sp => sp.GetRequiredService<IBlobStorage>());

// Or against a named/keyed blob store (resolves the keyed IBlobStorage + IBlobContainerManager)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(serviceKey: "keys");
```

## Configuration

No specific configuration. Depends on the underlying `IBlobStorage` configuration. Cloud/object-store providers should also register or pass the matching `IBlobContainerManager` so the `DataProtection` container is created before the first key write.

The blob data plane treats a missing container as an error (not auto-created), so a repository with no `IBlobContainerManager` can never fix a missing `DataProtection` container — the first key write on a fresh deployment would fail. This is now **enforced**, not just advised: whenever the effective manager is `null` and the storage reports `IBlobStorage.RequiresContainerProvisioning == true`, configuration throws `InvalidOperationException` (at call time for the storage-instance overload; at first options resolution for the DI/factory/keyed overloads) unless you pass `provisioning: BlobContainerProvisioning.PreProvisioned` to acknowledge that the container was provisioned out-of-band (portal, CLI, IaC).

Provisioning matrix:

| Scenario | What to do |
| --- | --- |
| Manager available (registered, keyed, or passed) | Nothing — the `DataProtection` container is ensured before writes; no acknowledgment is needed |
| No manager, provisioning-requiring backend (AWS, Azure, FileSystem, SSH) | Wire an `IBlobContainerManager` (preferred), or provision the container out-of-band and pass `provisioning: BlobContainerProvisioning.PreProvisioned` |
| Cloudflare R2 | `provisioning: BlobContainerProvisioning.PreProvisioned` is the only option — R2 deliberately ships no `IBlobContainerManager` (object-scoped tokens cannot create buckets); create the bucket in the Cloudflare dashboard/API first |
| Redis (`RequiresContainerProvisioning == false`) | Exempt — the backing hash materializes on first write; the storage-only overload works with no acknowledgment |

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Checks`
- `Azure.Extensions.AspNetCore.DataProtection.Blobs`
- `Microsoft.AspNetCore.DataProtection`

## Side Effects

- Configures `KeyManagementOptions.XmlRepository` to use blob storage
