# Headless.Blobs.Core

Unified setup builder for registering one or more named blob storages in a single dependency-injection container.

## Problem Solved

A single application often needs several blob stores at once — images on one backend, documents on another, scratch files on a third — and sometimes two instances of the same provider (a production and a staging bucket). Registering providers directly only yields one `IBlobStorage`; a second registration silently shadows the first. This package adds `AddHeadlessBlobs(...)`, a single entry point that composes an optional default plus any number of independently-configured named stores, each resolvable by name.

## Key Features

- `AddHeadlessBlobs(Action<HeadlessBlobsSetupBuilder>)` — the single registration entry point for all blob stores.
- Optional default store (at most one), injectable as a plain `IBlobStorage`.
- Unlimited named stores with unique names; the same provider may back several.
- `IBlobStorageProvider.GetStorage(name)` / `GetStorageOrNull(name)` plus keyed `IBlobStorage` resolution.
- Deferred, gate-validated registration: a misconfigured setup throws before mutating the service collection.

## Design Notes

Each provider package contributes `Use{Provider}` members on the builder — default overloads on `HeadlessBlobsSetupBuilder` and named overloads on `HeadlessBlobInstanceBuilder`. Named stores register as keyed `IBlobStorage` services, never touching the default (unkeyed) registration, so a named-only configuration leaves plain `IBlobStorage` unregistered. Each store is fully isolated: its own options (bound as named options), its own provider client, and its own `IBlobNamingNormalizer`. Presigned support is a per-store capability — the resolved store implements `IPresignedUrlBlobStorage` only when its provider supports it (AWS, Azure, Cloudflare R2).

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
    blobs.UseCloudflareR2(options => { /* ... */ });               // default store
    blobs.AddNamed("docs", instance => instance.UseAzure(options => { /* ... */ }));
    blobs.AddNamed("scratch", instance => instance.UseFileSystem(options => options.BaseDirectoryPath = "/tmp/blobs"));
});
```

Resolve stores:

```csharp
public sealed class FileService(IBlobStorage defaultStorage, IBlobStorageProvider provider)
{
    public IBlobStorage Docs => provider.GetStorage("docs");
    // or inject directly: [FromKeyedServices("docs")] IBlobStorage docs
}
```

## Configuration

No options of its own. Each store's options are configured through its provider's `Use{Provider}` overloads (`Action<TOptions>`, `IConfiguration`, or `Action<TOptions, IServiceProvider>`).

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Extensions`

## Side Effects

- Registers `IBlobStorageProvider` as a singleton.
- Registers a marker that rejects a second `AddHeadlessBlobs` call on the same service collection.
- Applies each provider contribution (default unkeyed `IBlobStorage`, named keyed `IBlobStorage`) only after setup gates pass.
