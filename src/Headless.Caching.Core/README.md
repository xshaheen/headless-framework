# Headless.Caching.Core

Shared factory-backed cache orchestration for cache providers.

## Problem Solved

Centralizes the `GetOrAddAsync` state machine so memory, Redis, and hybrid providers share the same factory execution, keyed locking, fail-safe fallback, timeout, background completion, and throttle behavior.

## Key Features

- `FactoryCacheCoordinator` - shared factory orchestration engine.
- `IFactoryCacheStore` - provider primitive for metadata-aware entry reads and writes.
- `CacheStoreEntry<T>` - logical and physical expiration snapshot used by the coordinator.
- `CacheStoreEntryExtensions` - shared `IsFresh`/`IsPhysicallyPresent` predicates so every provider and the coordinator agree on the expiration boundary (an entry is expired at the exact tick, `expiresAt <= now`).
- `FactoryCacheCoordinator.IsCallerCancellation` - shared predicate provider composites use so caller cancellation propagates while an unrelated/downstream `OperationCanceledException` activates fail-safe consistently.
- Fail-safe, factory timeout, and background completion logs.

## Design Notes

Providers construct the coordinator directly with their `TimeProvider` and logger; the Core package has no DI setup. Store read failures are treated as misses, and fail-safe restamp writes are best-effort so a stale value can still be returned when the backing store is unhealthy. Cancellation is classified by token identity: the caller's own cancellation propagates and never activates fail-safe, while an `OperationCanceledException` from an unrelated or downstream token is treated as a failure that activates fail-safe.

Factory timeout selection is centralized in the coordinator. If fail-safe is enabled, a stale reserve exists, and `FactorySoftTimeout` is finite, the soft timeout governs. Otherwise a finite `FactoryHardTimeout` governs. Otherwise factory execution is unbounded except for caller cancellation. A finite soft timeout also bounds acquisition of the same per-key lock when stale data exists, so waiters and supported same-key re-entrant calls return stale instead of blocking behind an in-flight refresh.

The coordinator deliberately diverges from FusionCache on background cancellation. A soft-timed-out factory uses a detached internal token and can outlive the caller request. Hard timeouts cancel or abandon the factory and never allow background completion. The per-key no-duplicate-factory guarantee holds cleanly for cooperative factories; after the background ceiling abandons a token-ignoring factory, another factory may run for that key while the abandoned task continues untracked, but late timeout-path writes are gated off.

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
