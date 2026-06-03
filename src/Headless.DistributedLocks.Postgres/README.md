# Headless.DistributedLocks.Postgres

## Problem Solved

Coordinates work across nodes using PostgreSQL advisory locks, with no Redis dependency and with transaction-coupled locking available for data mutations already protected by a PostgreSQL transaction.

## Key Features

- `AddPostgresDistributedLocks(...)` registers `IDistributedLockProvider` and `IDistributedReaderWriterLockProvider`.
- `PostgresAdvisoryLockKey` maps strings, `long`, and `(int, int)` keys onto PostgreSQL advisory key spaces.
- Session-scoped mutex locks use `pg_try_advisory_lock` and release with `pg_advisory_unlock`.
- Reader-writer locks use PostgreSQL shared and exclusive advisory locks.
- Mutex handles receive durable sequence-backed `FencingToken` values.
- `PostgresDistributedLock.AcquireWithTransactionAsync(...)` and `TryAcquireWithTransactionAsync(...)` use transaction-scoped `pg_advisory_xact_lock`.

## Design Notes

- Standard provider locks are session-scoped: they require a stable backend session from acquire through release. Use direct PostgreSQL connections or PgBouncer session pooling.
- Under PgBouncer transaction or statement pooling, use the transaction-coupled static API with a caller-owned `NpgsqlTransaction`; do not use session-scoped handles.
- Session-scoped locks have no TTL. `RenewAsync(...)` returns `true` and `GetExpirationAsync(...)` returns `null`.
- Postgres does not provide an N-holder advisory semaphore; use Redis semaphores or a separate slot-table design when N-holder concurrency is required.
- Prompt connection-death detection for an idle lock holder depends on Npgsql's `Keepalive`: a silently-dropped TCP connection only fires `StateChange` (which cancels `ConnectionScopedLockHandle.ConnectionLostToken`) once a keepalive probe fails. When the provider builds its own data source from `ConnectionString` it defaults `KeepAlive` (30s, configurable via `options.KeepAlive`) unless the connection string already sets one. If you inject your own `DataSource`, set `Keepalive` on it yourself. An application-level connection monitor is a planned follow-up that will remove this dependency.

## Installation

```bash
dotnet add package Headless.DistributedLocks.Postgres
```

## Quick Start

```csharp
builder.Services.AddPostgresDistributedLocks(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Postgres");
    options.KeyPrefix = "distributed-lock:";
});

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
options.ConnectionString = "...";        // required unless DataSource is set
options.DataSource = dataSource;         // preferred when already registered
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

- Registers `IDistributedLockProvider` as singleton.
- Registers `IDistributedReaderWriterLockProvider` as singleton.
- Registers Postgres storage, release signal, fencing-token source, `TimeProvider.System`, and `ILongIdGenerator` when absent.
