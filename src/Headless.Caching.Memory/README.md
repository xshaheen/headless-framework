# Headless.Caching.Foundatio.Memory

In-memory cache implementation using Foundatio for single-instance applications.

## Problem Solved

Provides high-performance in-memory caching using the unified `ICache` abstraction, suitable for single-instance deployments or as an L1 cache layer.

## Key Features

- Full `IInMemoryCache` implementation using Foundatio
- Can serve as default `ICache` or alongside distributed cache
- Supports strongly-typed `ICache<T>` pattern
- Automatic memory management with configurable limits
- Can act as `IDistributedCache` adapter for single-instance scenarios

## Installation

```bash
dotnet add package Headless.Caching.Foundatio.Memory
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// As default cache
builder.Services.AddInMemoryCache();

// Or with options
builder.Services.AddInMemoryCache(options =>
{
    options.MaxItems = 10000;
});

// As non-default (use alongside distributed cache)
builder.Services.AddInMemoryCache(isDefault: false);
```

## Configuration

### Options

```csharp
options.MaxItems = 10000;            // Maximum cached items
options.ShouldCloneValues = false;   // Clone values on get/set
```

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Hosting`
- `Foundatio`

## Side Effects

- Registers `IInMemoryCache` as singleton
- Registers `ICache` as singleton (if isDefault: true)
- Registers `IDistributedCache` adapter (if isDefault: true)
- Registers `ICache<T>` and `IInMemoryCache<T>` as singletons
