# Headless.Caching.Core

Shared factory-backed cache orchestration for cache providers.

## Problem Solved

Centralizes the `GetOrAddAsync` state machine so memory, Redis, and hybrid providers share the same factory execution, keyed locking, fail-safe fallback, and throttle behavior.

## Key Features

- `FactoryCacheCoordinator` - shared factory orchestration engine.
- `IFactoryCacheStore` - provider primitive for metadata-aware entry reads and writes.
- `CacheStoreEntry<T>` - logical and physical expiration snapshot used by the coordinator.
- Fail-safe activation log when stale data is served.

## Design Notes

Providers construct the coordinator directly with their `TimeProvider` and logger; the Core package has no DI setup. Store read failures are treated as misses, and fail-safe restamp writes are best-effort so a stale value can still be returned when the backing store is unhealthy.

## Installation

```bash
dotnet add package Headless.Caching.Core
```

## Quick Start

Consumers normally do not use this package directly. Provider packages reference it to implement `GetOrAddAsync`.

## Configuration

None.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Extensions`

## Side Effects

None. Providers own coordinator construction.
