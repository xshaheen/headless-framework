# Headless.DistributedLocks.Core.Database

## Problem Solved

Lets database providers map session-scoped or transaction-scoped lock primitives onto the standard distributed-lock abstractions without adding ADO.NET-specific machinery to Redis or cache providers.

## Key Features

- Internal `IConnectionScopedLockStorage` seam for non-blocking session-held lock acquisition and release.
- Internal `ConnectionScopedDistributedLock` engine implements `IDistributedLock` over connection-scoped storage.
- Internal `ConnectionScopedReadWriteLock` engine implements `IDistributedReadWriteLock` over shared/exclusive storage.
- Internal `IFencingTokenSource` seam lets database providers stamp mutex handles with durable sequence-backed fencing tokens.
- Internal `IReleaseSignal` seam provides the wake-up hook for provider push notifications plus polling fallback.

## Design Notes

- Connection-scoped locks have no TTL and no GC finalizer reclaim. `RenewAsync(...)` is a no-op success, `GetExpirationAsync(...)` returns `null`, and the lock is released only when the handle is disposed (or `ReleaseAsync()` is called). The provider holds a strong reference to the engine handle for its lifetime, so an abandoned handle leaks its connection and lock until the provider is disposed. Always `await using` the handle.
- Handle loss is backed by an active connection monitor, not just the connection's `StateChange` event: monitored handles (`CanObserveLoss == true`) run a periodic bounded-timeout server-side probe so a silent half-open connection cancels `LostToken` instead of going unnoticed until the next query.
- The engine optimistically multiplexes uncontended locks on distinct keys onto a shared physical connection and transparently falls back to a dedicated connection on contention or advisory-key collision. This is a performance characteristic; lock semantics are unchanged.
- Reader-writer locks do not issue fencing tokens; `FencingToken` is `null` for read and write handles.
- A composite acquisition (`AcquireAllAsync(...)` / `TryAcquireAllAsync(...)`, mutex or reader-writer) over N resources **pins N database connections for the whole duration of the hold**. Connection-scoped locks live only while their session does, so there is no TTL-backed lease that could hold a resource without a live connection — every child of the composite keeps one. Multiplexing does not remove this: contended children fall back to dedicated connections by design. Size the connection pool for the largest composite the application forms, and prefer small sets. This is an operational cost of the connection-scoped model, not a defect.

## Provider seams (internal)

The connection-scoped engine and its seams are **internal implementation infrastructure** shared by the first-party `Headless.DistributedLocks.PostgreSql` and `Headless.DistributedLocks.SqlServer` providers via `InternalsVisibleTo` — mirroring the sibling `Headless.Coordination.Core.Database` package. The engine (`ConnectionScopedDistributedLock` / `ConnectionScopedReadWriteLock`) owns all portable concerns (retry loop, acquire-timeout contract, jittered polling, waiter caps, fencing-token stamping); each first-party provider supplies three seams:

1. **`IConnectionScopedLockStorage`** — the storage seam. `TryAcquireAsync` does a single, non-blocking acquisition of the native primitive (for example `pg_advisory_lock`, `sp_getapplock`) and returns a live `ConnectionScopedLockHandle`, or `null` when the resource is held in a conflicting mode (the engine retries). Blocking-with-timeout is the engine's job.
2. **`IReleaseSignal`** — the wake-up seam between retry attempts, backed by a native push channel (for example Postgres `LISTEN/NOTIFY`) with **polling as the correctness fallback**: `WaitAsync` returns by `pollingFallback` even if no signal arrives, so a dropped or missed `PublishAsync` only costs latency — never a stuck acquirer. `PollingReleaseSignal` is the in-process default.
3. **`IFencingTokenSource`** *(optional)* — stamps exclusive locks with a monotonic fencing token from a durable, strictly-increasing sequence. Shared (reader) locks never request a token.

A third-party backend does not implement these seams; it implements the public `IDistributedLock` / `IDistributedReadWriteLock` abstractions from `Headless.DistributedLocks.Abstractions` directly.

## Installation

```bash
dotnet add package Headless.DistributedLocks.Core.Database
```

## Quick Start

Use a concrete provider such as `Headless.DistributedLocks.PostgreSql`; application code normally does not register `Core.Database` directly.

## Configuration

None directly. Concrete providers own options and storage configuration.

## Dependencies

- `Headless.DistributedLocks.Abstractions`
- `Headless.DistributedLocks.Core`
- `Headless.Core`
- `Headless.Hosting`

## Side Effects

None by itself. Concrete providers register the public lock providers.
