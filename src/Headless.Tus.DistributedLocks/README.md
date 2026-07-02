# Headless.Tus.DistributedLocks

Distributed lock-based TUS file lock provider, using `Headless.DistributedLocks` to prevent concurrent PATCH corruption across multiple application instances.

## Problem Solved

The TUS protocol allows only one concurrent PATCH per file. On single-instance deployments, `tusdotnet`'s default in-process locking suffices. On multi-instance deployments (load-balanced or Kubernetes pods), each instance has its own in-process lock table, so two nodes can simultaneously PATCH the same file, producing interleaved blocks and corrupted uploads. `DistributedLockTusLockProvider` uses the framework's `IDistributedLock` to coordinate across nodes.

## Key Features

- `DistributedLockTusLockProvider` — `ITusFileLockProvider` backed by `IDistributedLock`
- `DistributedLockTusFileLock` — `ITusFileLock` that calls `TryAcquireAsync` with zero wait; returns `false` immediately if another node holds the lock (tusdotnet returns `423 Locked` to the client)
- Lock resource key format: `{resourcePrefix}-{fileId}` with prefix default `tus-file-lock`; give each TUS endpoint its own prefix when several endpoints (different stores/containers) share one `IDistributedLock` backend, so equal file ids cannot contend for the same lock
- Compatible with any `IDistributedLock` backend (Redis, in-memory, etc.)
- Single `AddDistributedLockTusLockProvider(resourcePrefix?)` extension on `IServiceCollection`
- Best-effort mutual exclusion, not fencing: tusdotnet's `ITusFileLock` contract has no hook to observe a lease lost mid-request, so a holder that loses its lease during a backend partition keeps writing until the request ends — the auto-extending lease shrinks that window but cannot eliminate it

## Installation

```bash
dotnet add package Headless.Tus.DistributedLocks
```

## Quick Start

```csharp
using Headless.Tus;
using tusdotnet.Interfaces;
using tusdotnet.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Register a Headless distributed lock backend (Redis shown; any backend works)
builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

// 2. Register the TUS lock provider
builder.Services.AddDistributedLockTusLockProvider();

var app = builder.Build();

// 3. Wire the lock provider into the TUS configuration
app.MapTus(
    "/files",
    async ctx =>
    {
        var lockProvider = ctx.RequestServices.GetRequiredService<ITusFileLockProvider>();

        return new DefaultTusConfiguration
        {
            Store = tusStore, // your TusAzureStore instance
            UrlPath = "/files",
            FileLockProvider = lockProvider,
        };
    }
);

app.Run();
```

## Configuration

Registering an `IDistributedLock` provider is required. Optionally pass a lock resource-key prefix — `AddDistributedLockTusLockProvider("tus-avatars")` — when several TUS endpoints share one lock backend (default `tus-file-lock`). The lock acquires with `AcquireTimeout = TimeSpan.Zero` (non-blocking) and a finite, auto-extending lease (`Monitoring = AutoExtend`, provider-default TTL): a long upload keeps the lock alive, while a crashed holder's lease expires so the file is not stuck.

## Dependencies

- `Headless.Tus`
- `Headless.DistributedLocks.Abstractions`

## Side Effects

- `AddDistributedLockTusLockProvider()` registers `ITusFileLockProvider` as a singleton (`DistributedLockTusLockProvider`).
- Requires `IDistributedLock` to be registered in DI before this call.
