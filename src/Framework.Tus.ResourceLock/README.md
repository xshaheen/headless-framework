# Framework.Tus.ResourceLock

TUS file locking using Framework.ResourceLocks.

## Problem Solved

Provides a TUS file lock provider implementation using the framework's distributed resource locking, enabling concurrent upload coordination across multiple instances.

## Key Features

- `ResourceLockTusLockProvider` - ITusFileLockProvider implementation
- `ResourceLockTusLock` - Distributed file lock wrapper
- Works with any IResourceLockProvider (Redis, Cache)

## Installation

```bash
dotnet add package Framework.Tus.ResourceLock
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add resource lock provider first
builder.Services.AddResourceLock();
builder.Services.AddResourceLockRedisStorage();

// Add TUS lock provider
builder.Services.AddResourceLockTusLockProvider();

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

- `Framework.Tus`
- `Framework.ResourceLocks.Abstractions`
- `tusdotnet`

## Side Effects

- Registers `ITusFileLockProvider` as singleton
