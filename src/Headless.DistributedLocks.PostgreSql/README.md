# Headless.DistributedLocks.PostgreSql

## Problem Solved

Coordinates work across nodes using PostgreSQL advisory locks, with no Redis dependency and with transaction-coupled locking available for data mutations already protected by a PostgreSQL transaction.

## Key Features

- `UsePostgreSql(...)` registers `IDistributedLock` and `IDistributedReadWriteLock` through `AddHeadlessDistributedLocks(...)`.
- `PostgresAdvisoryLockKey` maps strings, `long`, and `(int, int)` keys onto PostgreSQL advisory key spaces.
- Session-scoped mutex locks use `pg_try_advisory_lock` and release with `pg_advisory_unlock`.
- Reader-writer locks use PostgreSQL shared and exclusive advisory locks.
- Mutex handles receive durable sequence-backed `FencingToken` values.
- `PostgresDistributedLock.AcquireWithTransactionAsync(...)` and `TryAcquireWithTransactionAsync(...)` use transaction-scoped `pg_advisory_xact_lock`.

## Design Notes

- Standard provider locks are session-scoped: they require a stable backend session from acquire through release. Use direct PostgreSQL connections or PgBouncer session pooling.
- Under PgBouncer transaction or statement pooling, use the transaction-coupled static API with a caller-owned `NpgsqlTransaction`; do not use session-scoped handles.
- Session-scoped locks have no TTL and no finalizer reclaim. `RenewAsync(...)` returns `true`, `GetExpirationAsync(...)` returns `null`, and the lock is released only when the handle is disposed or `ReleaseAsync()` is called. Always `await using` the handle; an abandoned handle leaks its connection and advisory lock until the provider is disposed.
- `Monitoring = LockMonitoringMode.None` leaves `LostToken` as `CancellationToken.None` and avoids the active connection probe. `Monitor` and `AutoExtend` both opt into connection-death observation; there is no TTL to extend.
- Resource-targeted inspection (`IsLockedAsync(resource)`, `GetLockInfoAsync(resource)`) can see remote holders because the caller supplies the advisory key. Provider-wide enumeration (`ListActiveLocksAsync()`, `GetActiveLocksCountAsync()`) remains local-handle only because `pg_locks` does not expose reversible resource names for the provider namespace once advisory keys are hashed.
- Postgres does not provide an N-holder advisory semaphore; use Redis semaphores or a separate slot-table design when N-holder concurrency is required. Because there is no semaphore here, **semaphore composites do not apply to this provider**. Mutex and reader-writer composites do.
- A composite acquisition (`AcquireAllAsync(...)` / `TryAcquireAllAsync(...)`) over N resources **pins N connections for the whole duration of the hold**, because these locks are connection-scoped and there is no TTL-backed lease to hold a resource without a live session. Size the connection pool for the largest composite the application forms, and prefer small sets. This is an operational cost of the connection-scoped model, not a defect.
- The provider multiplexes uncontended advisory locks on distinct keys onto a shared physical connection and falls back to a dedicated connection on contention or advisory-key collision. This lowers connection usage in the common case without changing lock semantics — but it does not reduce a composite's hold-time connection count, since contended children take dedicated connections by design.
- Connection-death detection for an idle lock holder is active: monitored handles (`LostToken`) run a periodic bounded-timeout server-side probe whose command timeout catches silently-dropped half-open connections that Npgsql's `StateChange` event alone would miss until the next operation. TCP keepalive is complementary, not redundant: when the provider builds its own data source from `ConnectionString` it defaults `KeepAlive` (30s, configurable via `options.KeepAlive`) unless the connection string already sets one, surfacing dead sockets faster at the transport layer. If you inject your own `DataSource`, set `Keepalive` on it yourself for the tightest detection window; the active monitor still operates regardless.

## Installation

```bash
dotnet add package Headless.DistributedLocks.PostgreSql
```

## Quick Start

```csharp
builder.Services.AddHeadlessDistributedLocks(setup =>
    setup.UsePostgreSql(options =>
    {
        options.ConnectionString = builder.Configuration.GetConnectionString("Postgres");
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
await using var connection = await dataSource.OpenConnectionAsync(ct);
await using var transaction = await connection.BeginTransactionAsync(ct);

await PostgresDistributedLock.AcquireWithTransactionAsync(
    PostgresAdvisoryLockKey.FromString("orders:123"),
    transaction,
    ct
);

// mutate protected rows, then commit or rollback to release the lock
await transaction.CommitAsync(ct);
```

## Configuration

```csharp
options.ConnectionString = "..."; // required unless DataSource is set
options.DataSource = dataSource; // preferred when already registered
options.KeyPrefix = "distributed-lock:";
options.PollingFallback = TimeSpan.FromMilliseconds(100);
options.EnablePushWakeup = true;
options.KeepAlive = TimeSpan.FromSeconds(30); // applied only to a provider-built DataSource
```

## Dependencies

- `Headless.DistributedLocks.Core.Database`
- `Headless.DistributedLocks.Core`
- `Headless.Hosting`
- `Npgsql`

## Side Effects

- Registers `IDistributedLock` as singleton.
- Registers `IDistributedReadWriteLock` as singleton.
- Registers Postgres storage, release signal, fencing-token source, `TimeProvider.System`, and `IGuidGenerator` when absent.
