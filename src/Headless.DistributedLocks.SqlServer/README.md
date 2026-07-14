# Headless.DistributedLocks.SqlServer

## Problem Solved

Coordinates work across nodes using SQL Server application locks, with native server-side blocking and transaction-coupled locking available for data mutations already protected by a SQL Server transaction.

## Key Features

- `UseSqlServer(...)` registers `IDistributedLock` and `IDistributedReadWriteLock` through `AddHeadlessDistributedLocks(...)`.
- Session-scoped mutex locks use `sp_getapplock` with `@LockMode = 'Exclusive'` and release with `sp_releaseapplock`.
- Reader-writer locks use SQL Server `Shared` and `Exclusive` application-lock modes.
- Mutex handles receive durable SQL `SEQUENCE`-backed `FencingToken` values when fencing is enabled.
- `SqlServerDistributedLock.AcquireWithTransactionAsync(...)` and `TryAcquireWithTransactionAsync(...)` use transaction-owned application locks.
- Resource names longer than SQL Server's 255-character `@Resource` limit are encoded as `sha256:<lowercase-hex>`.

## Design Notes

- Standard provider locks are session-scoped: the holding `SqlConnection` must stay open until release. Do not return that connection to arbitrary pooling code while the lock is held.
- SQL Server blocks waiters inside `sp_getapplock @LockTimeout`; there is no push-notification channel and no provider polling loop for contended acquires. The provider still receives a no-op release signal to satisfy its constructor contract; under server-side blocking that signal's `WaitAsync`/`PublishAsync` are never invoked.
- Session-scoped locks have no TTL. `RenewAsync(...)` returns `true` and `GetExpirationAsync(...)` returns `null`. The handle owns a live `SqlConnection`, so disposing it (directly or via `await using`) is the contract. Consistent with the connection-scoped disposal contract there is no GC finalizer reclaim, so a leaked, undisposed handle strands its connection (and the held applock and liveness-probe timer) until the provider is disposed.
- Connection-death detection backs the handle's lost token with two signals: the connection's `StateChange` event (clean disconnects) and an active bounded-timeout liveness probe (`SELECT 1` on a periodic cadence) that catches a silent half-open connection — a network drop with no RST where `StateChange` alone never fires until the next real query. This mirrors the intent of the multiplexing-engine providers' `ConnectionMonitor`, which this raw-`SqlConnection` storage cannot reuse directly.
- `IsLockedAsync(...)`, `IsReadLockedAsync(...)`, `IsWriteLockedAsync(...)`, and reader counts inspect SQL Server lock state for a specific resource. `GetLockIdAsync(...)`, `GetLockInfoAsync(...)`, `ListActiveLocksAsync(...)`, and `GetActiveLocksCountAsync(...)` report only handles owned by the current provider instance because SQL Server application locks do not expose Headless lock ids for remote sessions.
- Reader counts are presence-only for remote holders. `sp_getapplock`/`APPLOCK_TEST` expose the current lock mode but no holder count, so `GetReaderCountAsync(...)`/`GetLocksCountAsync(...)` count local (same-process) holders exactly but collapse any number of remote shared readers to `1`. Treat the remote value as held / not-held, not an exact count. (The Postgres provider counts `pg_locks` rows directly and does report exact cross-process counts — a deliberate per-backend difference.)
- Transaction-coupled locking is the safest primitive for SQL Server data mutations: commit or rollback releases the lock, and no explicit release is issued.
- The transaction-coupled API takes a `string` resource (`AcquireWithTransactionAsync("orders:123", ...)`), whereas the Postgres advisory-lock API takes a typed `PostgresAdvisoryLockKey`. This asymmetry is primitive-driven: `sp_getapplock` is natively string-keyed, while `pg_advisory_xact_lock` keys on a `bigint`, so the Postgres surface must expose the key type. Both encode `KeyPrefix + resource` identically to the session provider, so the two APIs mutually exclude on the same logical resource.
- SQL Server does not provide an N-holder semaphore here; use Redis semaphores or a future persistent slot-table design when N-holder concurrency is required. Because there is no semaphore here, **semaphore composites do not apply to this provider**. Mutex and reader-writer composites do.
- A composite acquisition (`AcquireAllAsync(...)` / `TryAcquireAllAsync(...)`) over N resources **pins N connections for the whole duration of the hold**, because session-scoped locks live only while their `SqlConnection` does and no TTL-backed lease can hold a resource without one. Size the connection pool for the largest composite the application forms, and prefer small sets. This is an operational cost of the connection-scoped model, not a defect.

## Installation

```bash
dotnet add package Headless.DistributedLocks.SqlServer
```

## Quick Start

```csharp
builder.Services.AddHeadlessDistributedLocks(setup =>
    setup.UseSqlServer(options =>
    {
        options.ConnectionString = builder.Configuration.GetConnectionString("SqlServer");
        options.KeyPrefix = "distributed-lock:";
    })
);

await using var lease = await lockProvider.AcquireAsync(
    "orders:123",
    new DistributedLockAcquireOptions
    {
        AcquireTimeout = TimeSpan.FromSeconds(10),
        Monitoring = LockMonitoringMode.Monitor,
    },
    ct
);
```

Transaction-coupled locking:

```csharp
await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync(ct);
await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

await SqlServerDistributedLock.AcquireWithTransactionAsync("orders:123", transaction, cancellationToken: ct);

// mutate protected rows, then commit or rollback to release the lock
await transaction.CommitAsync(ct);
```

## Configuration

```csharp
options.ConnectionString = "..."; // required
options.Schema = "dbo"; // fencing sequence schema
options.KeyPrefix = "distributed-lock:";
options.CommandTimeout = TimeSpan.FromSeconds(30);
options.EnableFencing = true;
```

## Dependencies

- `Headless.DistributedLocks.Core.Database`
- `Headless.DistributedLocks.Core`
- `Headless.Hosting`
- `Microsoft.Data.SqlClient`

## Side Effects

- Registers `IDistributedLock` as singleton.
- Registers `IDistributedReadWriteLock` as singleton.
- Registers SQL Server storage, fencing-token source, storage initializer, `TimeProvider.System`, and `IGuidGenerator` when absent. The provider is wired with a no-op release signal (not a polling loop) because SQL Server blocks contended acquires server-side, so the provider's wait loop is unreachable.
- Creates a sanitized SQL `SEQUENCE` for durable fencing when `EnableFencing` is `true`.
