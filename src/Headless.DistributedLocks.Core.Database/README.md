# Headless.DistributedLocks.Core.Database

## Problem Solved

Lets database providers map session-scoped or transaction-scoped lock primitives onto the standard distributed-lock abstractions without adding ADO.NET-specific machinery to Redis or cache providers.

## Key Features

- `IConnectionScopedLockStorage` for non-blocking session-held lock acquisition and release.
- `ConnectionScopedDistributedLockProvider` implements `IDistributedLockProvider` over connection-scoped storage.
- `ConnectionScopedReaderWriterLockProvider` implements `IDistributedReaderWriterLockProvider` over shared/exclusive storage.
- `IFencingTokenSource` lets database providers stamp mutex handles with durable sequence-backed fencing tokens.
- `IReleaseSignal` provides the wake-up seam for provider push notifications plus polling fallback.

## Design Notes

- Connection-scoped locks have no TTL. `RenewAsync(...)` is a no-op success, `GetExpirationAsync(...)` returns `null`, and handle loss is tied to the storage connection's loss token.
- Reader-writer locks do not issue fencing tokens; `FencingToken` is `null` for read and write handles.

## Implementing a custom DB provider

This package is an intentional **public extension point**: a third-party backend implements three seams and wires them into the shared `ConnectionScopedDistributedLockProvider`, which owns all portable concerns (retry loop, acquire-timeout contract, jittered polling, waiter caps, fencing-token stamping). You implement only the backend-specific pieces.

1. **`IConnectionScopedLockStorage`** — the storage seam. `TryAcquireAsync` does a single, non-blocking acquisition of the native primitive (for example `pg_advisory_lock`, `sp_getapplock`) and returns a live `ConnectionScopedLockHandle`, or `null` when the resource is held in a conflicting mode (the provider retries). It must not block waiting for the lock — blocking-with-timeout is the provider's job. Implement release (idempotent), the lock-count / is-locked queries, and the active-locks enumeration.
2. **`IReleaseSignal`** — the wake-up seam between retry attempts. Back it with a native push channel (for example Postgres `LISTEN/NOTIFY`) so a blocked acquirer wakes promptly on release. **Polling is the correctness fallback**: `WaitAsync` must return by `pollingFallback` even if no signal arrives, so a dropped or missed `PublishAsync` only costs latency — never a stuck acquirer. If you have no push channel, reuse the in-process `PollingReleaseSignal`.
3. **`IFencingTokenSource`** *(optional)* — stamps exclusive locks with a monotonic fencing token from a durable, strictly-increasing sequence. `NextAsync` returns `null` when no token applies (the handle is then unfenced). Shared (reader) locks never request a token. Omit the source entirely if your backend has no use for fencing.

Then compose them: construct a `ConnectionScopedDistributedLockProvider` with your storage, release signal, and (optionally) fencing source, and wrap it in a `ConnectionScopedReaderWriterLockProvider` to expose read/write locks. See `Headless.DistributedLocks.Postgres` for a complete worked example.

## Installation

```bash
dotnet add package Headless.DistributedLocks.Core.Database
```

## Quick Start

Use a concrete provider such as `Headless.DistributedLocks.Postgres`; application code normally does not register `Core.Database` directly.

## Configuration

None directly. Concrete providers own options and storage configuration.

## Dependencies

- `Headless.DistributedLocks.Abstractions`
- `Headless.DistributedLocks.Core`
- `Headless.Core`
- `Headless.Hosting`

## Side Effects

None by itself. Concrete providers register the public lock providers.
