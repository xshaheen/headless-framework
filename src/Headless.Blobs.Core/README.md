# Headless.Blobs.Core

Unified setup builder for composing one or more named blob stores in a single DI container.

## Problem Solved

A single application often needs several blob stores at once — images on one backend, documents on another, scratch files on a third — and sometimes two instances of the same provider (a production and a staging bucket). Registering providers directly only yields one `IBlobStorage`; a second registration silently shadows the first. This package adds `AddHeadlessBlobs(...)`, a single entry point that composes an optional default plus any number of independently-configured named stores, each resolvable by name.

## Key Features

- `AddHeadlessBlobs(Action<HeadlessBlobsSetupBuilder>)` — single registration entry point for all blob stores.
- Optional default store (at most one), injectable as plain `IBlobStorage`.
- Unlimited named stores with unique names; the same provider may back several.
- `IBlobStorageProvider` — resolves named stores by name; exposes `RegisteredNames` for safe pre-validation.
- Keyed `IBlobStorage` resolution via `[FromKeyedServices("name")]` or `GetRequiredKeyedService<IBlobStorage>("name")`.
- Deferred, gate-validated registration: a misconfigured setup (duplicate default, duplicate name, zero providers for a named store) throws before mutating the service collection.

## Design Notes

- Each provider package contributes `Use{Provider}` extension members on `HeadlessBlobsSetupBuilder` (default) and `HeadlessBlobInstanceBuilder` (named). Named stores register as keyed `IBlobStorage` services, never touching the default (unkeyed) registration, so a named-only configuration leaves plain `IBlobStorage` unregistered.
- Each store is fully isolated: its own named options, its own provider client, and its own `IBlobNamingNormalizer`. Ambient services (`IMimeTypeProvider`, `IClock`) are shared across stores.
- Two capabilities are surfaced differently, on purpose. Presigned support is a per-store cast: for named stores, AWS, Azure, and CloudflareR2 also register a keyed `IPresignedUrlBlobStorage` forward; for the default store, feature-detect by casting (`storage is IPresignedUrlBlobStorage`). Container management is a **separate** registration resolved from DI: AWS, Azure, FileSystem, Redis, and SSH register a default + keyed `IBlobContainerManager`, while CloudflareR2 registers none (so `GetKeyedService<IBlobContainerManager>` returns null for an R2 store) — this is why it cannot be an `is`-cast from the shared AWS storage type.
- `IBlobStorageProvider.RegisteredNames` contains only **named** instance names; the default/unnamed store is excluded. Use it to validate an externally-supplied name before calling `GetStorage` rather than probe-and-catch.

## Installation

```bash
dotnet add package Headless.Blobs.Core
```

Add at least one provider package (`Headless.Blobs.Aws`, `Headless.Blobs.Azure`, `Headless.Blobs.CloudflareR2`, `Headless.Blobs.FileSystem`, `Headless.Blobs.Redis`, or `Headless.Blobs.SshNet`).

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
{
    // Default store — injected as plain IBlobStorage.
    blobs.UseCloudflareR2(options =>
    {
        options.AccountId = builder.Configuration["R2:AccountId"]!;
        options.AccessKeyId = builder.Configuration["R2:AccessKeyId"]!;
        options.SecretAccessKey = builder.Configuration["R2:SecretAccessKey"]!;
    });

    // Named store — injected as keyed IBlobStorage("docs").
    blobs.AddNamed(
        "docs",
        instance =>
            instance.UseAzure(
                setupAction: options => { },
                clientFactory: _ => new BlobServiceClient(builder.Configuration["Azure:Docs:ConnectionString"])
            )
    );

    // Two named instances of the same provider, each with independent config.
    blobs.AddNamed("scratch", instance => instance.UseFileSystem(options => options.BaseDirectoryPath = "/tmp/blobs"));

    blobs.AddNamed(
        "archive",
        instance => instance.UseAws(options => { }, awsOptions: builder.Configuration.GetAWSOptions("AWS:Archive"))
    );
});
```

Resolve stores:

```csharp
// Default store — plain injection.
public sealed class UploadService(IBlobStorage storage) { }

// Named store — keyed injection.
public sealed class DocsService([FromKeyedServices("docs")] IBlobStorage docsStorage) { }

// Named store — via IBlobStorageProvider.
public sealed class MultiStoreService(IBlobStorageProvider provider)
{
    public IBlobStorage GetDocs() => provider.GetStorage("docs");

    public bool HasStore(string name) => provider.RegisteredNames.Contains(name);
}

// Named container management (AWS/Azure/FileSystem/Redis/SSH — resolved, null for R2).
public sealed class ProvisioningService([FromKeyedServices("docs")] IBlobContainerManager? docsManager)
{
    public ValueTask EnsureAsync(string container, CancellationToken ct) =>
        docsManager?.EnsureContainerAsync(container, ct) ?? ValueTask.CompletedTask;
}

// Named presigned URL (AWS/Azure/R2 only).
public sealed class PresignedService([FromKeyedServices("docs")] IPresignedUrlBlobStorage presigned)
{
    public Task<Uri> GetDownloadUrl(BlobLocation location) =>
        presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.FromHours(1)).AsTask();
}
```

## Configuration

No options of its own. Each store's options are configured through its provider's `Use{Provider}` overloads (`Action<TOptions>`, `IConfiguration`, or `Action<TOptions, IServiceProvider>`).

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Extensions`

## Side Effects

- Registers `IBlobStorageProvider` as singleton (backed by the container's keyed `IBlobStorage` registrations).
- Registers a called-once marker that rejects a second `AddHeadlessBlobs` call on the same service collection.
- Default `Use{Provider}`: registers `IBlobStorage` as unkeyed singleton.
- `AddNamed(... Use{Provider})`: registers `IBlobStorage` as keyed singleton (`name`). For AWS, Azure, and CloudflareR2 also registers `IPresignedUrlBlobStorage` as keyed singleton (`name`). For AWS, Azure, FileSystem, Redis, and SshNet also registers `IBlobContainerManager` (default + keyed `name`); CloudflareR2 registers none. For SshNet, also registers a keyed internal SFTP connection pool singleton (`name`).
- There is no global (unkeyed) `IPresignedUrlBlobStorage` registration.
