---
domain: Idempotency
packages: Idempotency.Abstractions, Idempotency.Core, Idempotency.InMemory, Idempotency.PostgreSql, Idempotency.SqlServer
---

# Idempotency

> Durable, tenant-scoped idempotent admission: admit a key once across processes, replay its stored result on retry, and refuse a stale attempt's completion. Each record row carries its own lease and generation.

## How this differs from locks and fenced leases

Idempotent admission answers "has this operation already happened, and what was its result?". It admits one attempt per tenant-scoped key and fingerprint and replays the stored result to every later retry. It does not serialize different keys over a shared resource: use a [distributed lock](distributed-locks.md) for "one process at a time", and a [fenced lease](fencing.md) for ownership of work with no result to replay, such as a resource handed to an external executor. Comparison of all four primitives: [Choosing a coordination primitive](index.md#choosing-a-coordination-primitive).

## Orientation

Register one provider; nothing else is required:

```csharp
builder.Services.AddHeadlessIdempotency(setup => setup.UsePostgreSql(connectionString)); // or setup.UseSqlServer(...)
// tests, local development, one instance: builder.Services.AddHeadlessIdempotency(setup => setup.UseInMemory());
```

Each key's record row holds the admitted attempt's lease itself (a `generation` plus `lease_expires_at`), the same shape Stripe idempotency keys and AWS Powertools idempotency use. There is no second table, so every call locks exactly one row, and there is no lock order to get wrong. Idempotency does not depend on [`Headless.Fencing`](fencing.md).

- **Autonomous** — inject `IIdempotentOperations` and call `AdmitAsync`, `CompleteAsync`, `ReleaseAsync`, `RenewAsync`, or `PeekAsync`. Admission and completion each commit before they return; `PeekAsync` is a cheap, lock-free status read with no transaction of its own.
- **Enlisted** — `unit.Idempotency` (namespace `Headless.UnitOfWork`, added by `Headless.Idempotency.Abstractions`) runs `AdmitAsync`, `CompleteAsync`, `ReleaseAsync`, and `FenceAsync` inside the caller's own transaction, so a rollback leaves no record.

A message consumer admitting an operation once across redeliveries:

```csharp
var fingerprint = IdempotencyFingerprint.Compute(canonicalRequestBytes);
var admission = await operations.AdmitAsync(key, fingerprint, expectedContract: "orders.receipt/v1", ct);

switch (admission.Disposition)
{
    case IdempotentDisposition.Replay:
        return admission.Result!.Deserialize(ReceiptJsonContext.Default.Receipt);
    case IdempotentDisposition.InFlight:
        return null; // redeliver later
    case IdempotentDisposition.Conflict:
        throw new InvalidOperationException("Idempotency key reused with a different request.");
    case IdempotentDisposition.Admitted:
        return await factory.RunAsync(db, async (unit, ct) =>
        {
            await unit.Idempotency.FenceAsync(admission, ct);   // holds the record row until commit
            var receipt = DoWork();
            await unit.Idempotency.CompleteAsync(admission, receipt, ReceiptJsonContext.Default.Receipt, contract: "orders.receipt/v1", cancellationToken: ct);
            return receipt;
        }, cancellationToken: ct);
}
```

`Headless.Api.Idempotency` is the HTTP composition of this same store; see [§ HTTP composition](#http-composition) and [api.md § Headless.Api.Idempotency](api.md#headlessapiidempotency).

## Agent Rules

- Fence an admitted operation's writes through `unit.Idempotency.FenceAsync(admission)` as the unit's first idempotency call. It takes the record row's update-intent lock (never a shared one) and holds it until the unit ends, so no admission can take the key over while the unit's writes are uncommitted.
- `IsTakeover` means an earlier attempt was admitted for this key and ended without completing or releasing — it crashed or stalled past its lease. Its partial side effects may already exist. An operation that is not naturally safe to repeat must check `IsTakeover` before redoing them.
- `CompleteAsync` and `ReleaseAsync` both take the whole `IdempotentAdmission`, not just the key — the admission carries the `Generation` the completion is fenced against. A `StaleAdmissionException` from `CompleteAsync` means the attempt no longer owns the key (`Reason`: `Expired`, `Stale` after a takeover or purge, `Completed` for a second completion by the same attempt, or `Released`); nothing was stored, and a stored result is never overwritten.
- A stored fingerprint from an algorithm this version does not know (`IdempotencyFingerprint.IsKnownAlgorithm` is `false`) is refused with `NotSupportedException`, never recomputed — the request that produced it is gone, so guessing could replay a wrong result or reject a legitimate retry.
- `expectedContract` on `AdmitAsync` is a read guard, not a write guard: a completed record stored under a different contract tag comes back as `Conflict` (`StoredContract` set) rather than a mis-typed replay. Pass the same contract tag to `AdmitAsync` and `CompleteAsync`.
- `RenewAsync` is one guarded single-row update the store commits on its own connection — call it from a heartbeat loop without an owned unit. It extends the lease only while the record is pending at the admission's generation with a live lease, and otherwise reports why (`IdempotentLeaseRenewal.Status`). It is the one autonomous-only member; there is no `unit.Idempotency.RenewAsync`.
- The idempotency key, tenant id, and contract pass through `IdempotencyFieldLimits` (`TenantIdMaxLength` 128, `KeyMaxLength` 256, `ContractMaxLength` 256, `FingerprintAlgorithmMaxLength` 32, `FingerprintMaxLength` 64) before any SQL runs, and a lease duration must fall within `IdempotentOperationsOptions.MinimumLeaseDuration`/`MaximumLeaseDuration`.

## Core Concepts

### Admission

`AdmitAsync(key, fingerprint, expectedContract?, leaseDuration?, retention?, ct)` returns an `IdempotentAdmission` whose `Disposition` is one of:

| Disposition | Meaning | What is set |
| --- | --- | --- |
| `Admitted` | The caller owns the operation under a lease on its record. | `Generation`, `LeaseExpiresAt`, `IsTakeover`, `Retention` |
| `InFlight` | A live attempt owns the key; retry later. | `Generation` and `LeaseExpiresAt` (the live holder's) |
| `Replay` | The operation already completed within retention. | `Result` (`IdempotentResult`: `Payload`, `Contract`) |
| `Conflict` | The key is stored with a different fingerprint, or a completed result carries a contract the caller did not expect. | `StoredFingerprint`, and `StoredContract` for a contract mismatch |

Under the hood, admission locks or inserts the key's record row (one row, one lock), reads the database clock after the lock is held, then decides in order: fingerprint mismatch → `Conflict`; `Completed` within retention → `Replay` (or contract `Conflict` if `expectedContract` does not match); `Pending` with a generation whose lease is still live → `InFlight`; otherwise it grants: draws the next generation from the store-wide `record_generations` sequence, sets `lease_expires_at` to the database clock plus the lease duration, and returns `Admitted`. A record past its retention is reset in place and granted, unless a live attempt still holds it (its lease was renewed past the retention). `IsTakeover` is set when the record was `Pending` with a generation before this call, meaning an attempt ended without completing or releasing.

Generations only grow per key, even after the record is purged and the key admitted again, because they come from one sequence, drawn only after the row lock is held. A zombie attempt of a purged record can never match a new one.

### Fingerprint

`IdempotencyFingerprint` is a versioned digest of the request an idempotency key stands for: two admissions of one key must carry the same fingerprint, or the second is a `Conflict`. `IdempotencyFingerprint.V1` is SHA-256 over a caller-supplied canonical payload — the caller owns canonicalization (sorted fields, a fixed number format) so that two requests it considers equal produce identical bytes. `Compute(ReadOnlySpan<byte>)` and `Compute(string)` (UTF-8) compute it; `Matches(stored)` compares against a record's stored fingerprint and throws `NotSupportedException` for an unknown stored algorithm rather than guessing.

### Result and contract

`IdempotentResult` is opaque `Payload` bytes plus a `Contract` tag — the store never interprets them. `IdempotentOperationsJsonExtensions` adds typed helpers over both `IIdempotentOperations` and `UnitOfWorkIdempotency`: `CompleteAsync<T>(admission, result, JsonTypeInfo<T> typeInfo, contract, ...)` serializes to UTF-8 JSON before storing, and `IdempotentResult.Deserialize<T>(JsonTypeInfo<T>)` reads it back. Both take source-generated `JsonTypeInfo<T>` rather than reflection-based options, so they work under trimming and native AOT.

### Retention and purge

`IdempotentOperationsOptions.DefaultRetention` (24 hours) is how long a completed record replays, and `DefaultLeaseDuration` (2 minutes) is how long an admitted attempt owns its key unless renewed — both apply when the caller does not pass an explicit value to `AdmitAsync`. Every admission and renewal duration must fall within `MinimumLeaseDuration` (1 second) and `MaximumLeaseDuration` (1 day). `IdempotencyRetentionService`, a hosted service always registered by `AddHeadlessIdempotency`, deletes records past `retention_until` whose lease is no longer live, in batches of `PurgeBatchSize` (default 1000) every `PurgeInterval` (default 1 hour; `null` disables the service entirely). A pending record whose attempt renewed its lease past the retention is kept until that lease expires, so a live attempt never loses the record it will complete.

### Enlistment and the fence

Enlisted calls (`unit.Idempotency.*`) follow the same rules as `unit.Leases` and `unit.Sequences` (see [unit-of-work.md](unit-of-work.md)): `RequireTransaction`, then `ValidateEnlistment`, then — for admit, complete, and release, never for the fence read — mark an observed-mode unit non-retryable. What the unit must carry is the provider's judgment: the relational providers need a live transaction on their own database, while the in-memory provider refuses any unit over a database connection and accepts a resource-less unit (`IUnitOfWorkFactory.BeginAsync()`), which then plays the transaction. `unit.Idempotency.FenceAsync` takes the record row with an update-intent lock (`FOR NO KEY UPDATE` on PostgreSQL, `UPDLOCK, HOLDLOCK, ROWLOCK` on SQL Server) held until the unit ends, then refuses with `StaleAdmissionException` unless the record is still pending at the admission's generation with a live lease by the database clock read after the lock. A concurrent admission of the key waits on that row and then sees the unit's committed outcome. The lock is never shared: a waiting admission would queue for the update lock behind it, and the fencing unit's own completion would then deadlock.

## HTTP composition

`Headless.Api.Idempotency` is a thin HTTP adapter over this store — see [api.md § Headless.Api.Idempotency](api.md#headlessapiidempotency) for its options and setup. What it adds on top of `IIdempotentOperations`:

- **Store key.** The SHA-256 hex of a derived scope string (user, method, path, query, header key) — always 64 characters, so a long path or a 255-character header value stays within `IdempotencyFieldLimits.KeyMaxLength`. The store separately scopes every key by the current tenant.
- **Admitted.** The middleware runs the handler behind a lease-renewal loop (`RenewAsync` every third of the configured lease duration, each call bounded by that same interval, since a handler's own fenced transaction can block a renewal on the record row), then completes with the captured response once `ShouldCacheResponse` accepts it, or releases otherwise.
- **The post-commit completion window.** Completion runs *after* the handler's unit commits, not inside it — there is no enlisted HTTP completion today. A crash between the handler's commit and the middleware's completion call leaves the record `Pending`; the next request is `Admitted` as a takeover (`IsTakeover = true`) and re-runs the handler. A handler reads its admission with `HttpContext.GetIdempotencyContext()` (`IIdempotencyContext`) and either checks `IsTakeover` before repeating a side effect that is not safe to redo, or fences its own writes with `unit.Idempotency.FenceAsync(context.Admission)` so a still-running previous attempt (past its own takeover) cannot also commit.
- **Replay.** Writes the stored response verbatim (status, allowlisted headers, body). **Conflict.** Returns the configured mismatch status.
- **InFlight.** `Reject` (default): `409 g:idempotency_in_flight`. `WaitAndReplay`: each tick of a doubling backoff calls the cheap `PeekAsync` first and only re-runs the full, row-locking `AdmitAsync` when the peek shows the record settled or the holder's last-known lease expiry has passed — until `Replay`, `Admitted` (a takeover), or the configured timeout: `409 g:idempotency_in_flight_timeout`.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Idempotency.PostgreSql` | Records must survive restarts and be shared by every instance, and enlisted units run on PostgreSQL | Units run on SQL Server | Insert-or-lock with `ON CONFLICT DO NOTHING`; `clock_timestamp()` decides |
| `Headless.Idempotency.SqlServer` | The same, with units on SQL Server | — | `UPDLOCK, HOLDLOCK` insert-or-lock without `TRY/CATCH`; `SYSUTCDATETIME()` decides |
| `Headless.Idempotency.InMemory` | Tests, local development, or a single-instance host with no relational database (for example Redis-only) | Several instances serve the same keys, or retries must be deduplicated across a restart | Records live in one process and vanish on restart; the registered `TimeProvider` decides leases and retention; enlisted calls run only on resource-less units, so a fence cannot join a database transaction |

---

## Headless.Idempotency.Abstractions

Consumer contracts: `IIdempotentOperations`, `IdempotentAdmission`, `IdempotencyFingerprint`, `IdempotencyKey`, `IdempotentResult`, the JSON helpers, and the `unit.Idempotency` accessor.

### Setup

```bash
dotnet add package Headless.Idempotency.Abstractions
```

Reference it from code that admits, completes, or fences idempotent operations. Registration lives in `Headless.Idempotency.Core` and the provider packages.

### Design and runtime behavior

- `IIdempotentOperations.AdmitAsync(key, fingerprint, expectedContract?, leaseDuration?, retention?, ct)` → `IdempotentAdmission`. `CompleteAsync(admission, result, contract, retention?, ct)` stores the result and ends the lease in one transaction; throws `StaleAdmissionException` when the attempt no longer owns the key. `ReleaseAsync(admission, ct)` → `IdempotentLeaseStatus` (`Released` on success or when the key is already released; `Expired`, `Stale`, or `Completed` for a refusal that wrote nothing). `RenewAsync(admission, duration, ct)` → `IdempotentLeaseRenewal(Status, ExpiresAt)`, with `IsRenewed` when `Status` is `Current` (autonomous only). `PeekAsync(key, ct)` → `IdempotencyPeekStatus` (`Absent`, `Pending`, `Completed`; a record past its retention reads as `Absent`) — no row lock, no lease read, and no full disposition; it exists to poll cheaply while waiting on another attempt, not to replace `AdmitAsync`.
- `IdempotentAdmission`: `Disposition`, `Key` (`IdempotencyKey(TenantId, Key)`), `Fingerprint`, `Generation`/`LeaseExpiresAt` (when relevant), `IsTakeover`, `Retention`, `Result`, `StoredFingerprint`, `StoredContract`, and `IsAdmitted` (`[MemberNotNullWhen(true, nameof(Generation), nameof(LeaseExpiresAt), nameof(Retention))]`).
- `IdempotentLeaseStatus` is what the store found for an attempt's generation: `Current`, `Expired`, `Stale`, `Completed`, or `Released`. `StaleAdmissionException` (an `InvalidOperationException`) carries the refused `Key`, `Generation`, and `Reason`.
- `unit.Idempotency` (namespace `Headless.UnitOfWork`) returns a `UnitOfWorkIdempotency` bound to the unit; repeated reads on one unit return the same instance. It mirrors `AdmitAsync`/`CompleteAsync`/`ReleaseAsync` plus `FenceAsync(admission, ct)`, which throws `StaleAdmissionException` rather than returning a status. There is no enlisted `RenewAsync`.
- `unit.Idempotency` throws `InvalidOperationException` naming `AddHeadlessIdempotency` when no provider registered the feature.

---

## Headless.Idempotency.Core

Registration, fingerprint and key resolution, admission orchestration, and the retention purge.

### Setup

```bash
dotnet add package Headless.Idempotency.Core
```

Applications reach it through a provider package, as shown in [Orientation](#orientation).

### Configuration

| Builder member | Effect |
| --- | --- |
| `ConfigureOptions(Action<IdempotentOperationsOptions>)` | Sets `DefaultRetention` (24 hours), `DefaultLeaseDuration` (2 minutes), `MinimumLeaseDuration` (1 second), `MaximumLeaseDuration` (1 day), `PurgeInterval` (1 hour; `null` disables the purge), `PurgeBatchSize` (1000) |
| `ConfigureStorage(Action<IdempotencyStorageOptions>)` / `ConfigureStorage(IConfiguration)` | Sets `Schema` (default `"idempotency"`), the schema the record table lives in |

### Design and runtime behavior

- `AddHeadlessIdempotency` requires exactly one `Use…` provider call and nothing else. It swaps the `NullCurrentTenant` fallback for the `AsyncLocal`-backed `CurrentTenant` unless the host registered its own, because records are keyed by the current tenant.
- It registers `IIdempotentOperations`, `IUnitOfWorkIdempotency`, and `IdempotencyRequestResolver` as singletons, and always adds `IdempotencyRetentionService` as a hosted service — when `PurgeInterval` is `null` the service stays idle on a fixed one-minute re-check cadence instead of exiting, so a later reload back to a value resumes purging without restarting the host.
- `IIdempotencyRecordStore` is the provider seam. Applications do not call it.

---

## Headless.Idempotency.InMemory

Process-memory storage for idempotency records, for tests, local development, and single-instance hosts.

### Setup

```bash
dotnet add package Headless.Idempotency.InMemory
```

```csharp
builder.Services.AddHeadlessIdempotency(setup => setup.UseInMemory());
```

`UseInMemory()` takes no options. It registers `TimeProvider.System` and the unit-of-work factory when the host has not; register a `FakeTimeProvider` first to drive leases and retention in tests. `ConfigureStorage` has no effect on it.

### Design and runtime behavior

- Records live in a singleton table in this process and disappear when it stops. They deduplicate only the requests this process handles: behind a load balancer each instance keeps its own records, so the same key can run once per instance. Use a relational provider when several instances serve the same keys.
- The registered `TimeProvider` decides lease expiry and retention, read after the record's lock is held. A lease or retention that ends at the clock's instant has already ended.
- Every admission, fence, completion, and release takes an exclusive per-key lock, so the admission decision table (conflict, replay, in flight, takeover, reset past retention) runs on one consistent record, exactly as on the relational providers. Generations come from one process-wide counter, so they grow per key across releases, takeovers, and purges, but restart from 1 when the process restarts, when every earlier record is gone too.
- Autonomous calls run in a resource-less owned unit the store begins. Enlisted calls (`unit.Idempotency`) run only on a resource-less unit (`IUnitOfWorkFactory.BeginAsync()`); a unit over a database connection or `DbContext` is refused with `InvalidOperationException`, because in-memory records cannot commit or roll back with that transaction. The unit is the commit boundary: the record's lock is held until the unit ends, its writes reach the table only when the unit completes, and a rollback or a dispose without completing drops them. One unit may fence and then complete the same record without waiting on itself.
- `FenceAsync` holds the record until the unit ends, so no admission in this process can take the key over under the unit. It guards nothing outside the process and is not coupled to any database transaction.
- `PeekAsync` reads the committed record without the lock and never sees a unit's uncommitted writes. `RenewAsync` takes the lock and commits at once.
- There is no deadlock detection. Units that lock several keys must lock them in one consistent order, and callers should pass a cancellation token that bounds the wait.
- The retention purge skips a record a unit holds and keeps a record whose lease is still live, like the relational providers.
- `Headless.Api.Idempotency` works on it unchanged: `AddHeadlessIdempotency(setup => setup.UseInMemory())` plus `AddIdempotency(...)` needs no database.

---

## Headless.Idempotency.PostgreSql

PostgreSQL storage for idempotency records.

### Setup

```bash
dotnet add package Headless.Idempotency.PostgreSql
```

```csharp
builder.Services.AddHeadlessIdempotency(setup => setup.UsePostgreSql(connectionString));
```

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database that holds the records; an enlisted call is accepted only on a unit whose connection reaches it |
| `CommandTimeout` | 30 seconds | Also bounds how long a concurrent admission of the same key waits behind an open enlisted admission, fence, or completion |
| `InitializeOnStartup` | `true` | When `false`, the application creates the schema, generation sequence, record table, and index |

### Design and runtime behavior

- Record insert-or-lock never raises a unique-violation: `INSERT … ON CONFLICT DO NOTHING` followed by `SELECT … FOR NO KEY UPDATE`. Catching `23505` inside a unit would otherwise poison the PostgreSQL transaction (`25P02`).
- Every decision reads `clock_timestamp()` after the row lock, captured once per statement in a `MATERIALIZED` CTE — never `now()`, which is frozen at transaction start and would keep an expired lease looking live inside a long enlisted unit. `nextval()` runs only in a statement after the lock is held.
- The autonomous path begins an owned unit through `IIdempotencyRecordStore.BeginOwnedUnitAsync` at READ COMMITTED; the enlisted path runs on the unit's own connection and transaction with no retry.
- The initializer serializes concurrent hosts with an advisory lock and creates the schema and table idempotently.

---

## Headless.Idempotency.SqlServer

SQL Server storage for idempotency records.

### Setup

```bash
dotnet add package Headless.Idempotency.SqlServer
```

```csharp
builder.Services.AddHeadlessIdempotency(setup => setup.UseSqlServer(connectionString));
```

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database that holds the records; an enlisted call is accepted only on a unit whose connection reaches it. Name the database explicitly (`Initial Catalog`) |
| `CommandTimeout` | 30 seconds | Also bounds how long a concurrent admission of the same key waits behind an open enlisted admission, fence, or completion |
| `InitializeOnStartup` | `true` | When `false`, the application creates the schema, generation sequence, record table, and index |

### Design and runtime behavior

- Record insert-or-lock never raises a unique-violation: `SELECT … WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` then `IF NOT EXISTS INSERT`, with no `TRY/CATCH` — a caught duplicate key would doom an `XACT_ABORT` caller transaction.
- Each batch captures `SYSUTCDATETIME()` into a variable after the locking read. A generation is drawn with `SET @g = NEXT VALUE FOR …` into a variable after the lock, because `NEXT VALUE FOR` is illegal inside `CASE`, `OUTPUT`, `WHERE`, subqueries, and `MERGE`.
- The autonomous path begins an owned unit through `IIdempotencyRecordStore.BeginOwnedUnitAsync` at READ COMMITTED; the enlisted path runs on the unit's own connection and transaction with no retry.
- The initializer serializes concurrent hosts with `sp_getapplock` and creates the schema and table idempotently.
