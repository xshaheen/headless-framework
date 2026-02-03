# Headless.Tus.DistributedLock

TUS file locking using Headless.DistributedLocks.

## Problem Solved

Provides a TUS file lock provider implementation using the framework's distributed resource locking, enabling concurrent upload coordination across multiple instances.

## Key Features

- `DistributedLockTusLockProvider` - ITusFileLockProvider implementation
- `DistributedLockTusLock` - Distributed file lock wrapper
- Works with any IDistributedLockProvider (Redis, Cache)

## Installation

```bash
dotnet add package Headless.Tus.DistributedLock
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add resource lock provider first
builder.Services.AddDistributedLock();
builder.Services.AddDistributedLockRedisStorage();

// Add TUS lock provider
builder.Services.AddDistributedLockTusLockProvider();

var app = builder.Build();

app.MapTus("/files", async ctx =>
{
    var lockProvider = ctx.RequestServices.GetRequiredService<ITusFileLockProvider>();

    return new DefaultTusConfiguration
    {
        Store = store,
        UrlPath = "/files",
        FileLockProvider = lockProvider
    };
});
```

## Configuration

No additional configuration required beyond resource lock setup.

## Dependencies

- `Headless.Tus`
- `Headless.DistributedLocks.Abstractions`
- `tusdotnet`

## Side Effects

- Registers `ITusFileLockProvider` as singleton
