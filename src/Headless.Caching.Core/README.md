# Headless.Caching.Core

Shared factory-backed cache orchestration for cache providers.

## Problem Solved

Centralizes the `GetOrAddAsync` state machine so memory, Redis, and hybrid providers share the same factory execution, keyed locking, fail-safe fallback, and throttle behavior.

## Key Features

- `FactoryCacheCoordinator` - shared factory orchestration engine.
- `IFactoryCacheStore` - provider primitive for metadata-aware entry reads and writes.
- `CacheStoreEntry<T>` - logical, physical, and optional sliding expiration snapshot used by the coordinator.
- `CacheStoreEntryExtensions` - shared `IsFresh`/`IsPhysicallyPresent` predicates so every provider and the coordinator agree on the expiration boundary (an entry is expired at the exact tick, `expiresAt <= now`).
- `FactoryCacheCoordinator.IsCallerCancellation` - shared predicate provider composites use so caller cancellation propagates while an unrelated/downstream `OperationCanceledException` activates fail-safe consistently.
- Fail-safe activation log when stale data is served.

## Design Notes

Providers construct the coordinator directly with their `TimeProvider` and logger; the Core package has no DI setup. Store read failures are treated as misses, fail-safe restamp writes are best-effort, and sliding re-arm writes are best-effort so a cached value can still be returned when the backing store is unhealthy. Cancellation is classified by token identity: the caller's own cancellation propagates and never activates fail-safe, while an `OperationCanceledException` from an unrelated or downstream token is treated as a failure that activates fail-safe. Sliding expiration and fail-safe are rejected together because one needs value reads to extend the logical deadline while the other needs logical expiration to expose a stale reserve.

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
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

None. Providers own coordinator construction.
