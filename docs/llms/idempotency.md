---
domain: Idempotency
packages: Idempotency.Abstractions, Idempotency.Core, Idempotency.PostgreSql, Idempotency.SqlServer
---

# Idempotency

> Durable, tenant-scoped idempotent admission over a fenced lease: admit a key once across processes, replay its stored result on retry, and refuse a stale attempt's completion.

## Orientation

Every admission holds a fenced lease from [`Headless.Fencing`](fencing.md), so register fencing first, on the same database:

```csharp
builder.Services.AddHeadlessFencing(setup => setup.UsePostgreSql(connectionString));
builder.Services.AddHeadlessIdempotency(setup => setup.UsePostgreSql(connectionString)); // same database as fencing
```

`AddHeadlessIdempotency` throws `InvalidOperationException` at startup if no `IFencedLeases` is registered yet.

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
            await unit.Idempotency.FenceAsync(admission, ct);   // record, then lease
            var receipt = DoWork();
            await unit.Idempotency.CompleteAsync(admission, receipt, ReceiptJsonContext.Default.Receipt, contract: "orders.receipt/v1", cancellationToken: ct);
            return receipt;
        }, cancellationToken: ct);
}
```

`Headless.Api.Idempotency` is the HTTP composition of this same store; see [§ HTTP composition](#http-composition) and [api.md § Headless.Api.Idempotency](api.md#headlessapiidempotency).

## Agent Rules

- Fence an admitted operation's writes through `unit.Idempotency.FenceAsync(admission)`, never through `unit.Leases.FenceAsync(admission.Lease)` directly. Every idempotency call locks the key's record before its lease; locking them in the reverse order (lease, then record) deadlocks against a concurrent admission of the same key that is doing it the correct way round.
- `IsTakeover` means an earlier attempt was admitted for this key and ended without completing or releasing — it crashed, stalled past its lease, or a sweep abandoned it. Its partial side effects may already exist. An operation that is not naturally safe to repeat must check `IsTakeover` before redoing them.
- `CompleteAsync` and `ReleaseAsync` both take the whole `IdempotentAdmission`, not just the key — the admission carries the lease generation the completion is fenced against. A `StaleLeaseException` from `CompleteAsync` means a later admission already took the key over; nothing was stored, and only the winning attempt's result will be.
- A stored fingerprint from an algorithm this version does not know (`IdempotencyFingerprint.IsKnownAlgorithm` is `false`) is refused with `NotSupportedException`, never recomputed — the request that produced it is gone, so guessing could replay a wrong result or reject a legitimate retry.
- `expectedContract` on `AdmitAsync` is a read guard, not a write guard: a completed record stored under a different contract tag comes back as `Conflict` (`StoredContract` set) rather than a mis-typed replay. Pass the same contract tag to `AdmitAsync` and `CompleteAsync`.
- `RenewAsync` touches only the lease, never the record — call it from a heartbeat loop without an owned unit or a record lock. It is the one autonomous-only member; there is no `unit.Idempotency.RenewAsync`.
- The idempotency key, kind, and resource all pass through `IdempotencyFieldLimits` (`KeyMaxLength` 256, `ContractMaxLength` 256, `FingerprintAlgorithmMaxLength` 32, `FingerprintMaxLength` 64) and `FencingFieldLimits.ResourceMaxLength` (the key is also the lease's resource) before any SQL runs.

## Core Concepts

### Admission

`AdmitAsync(key, fingerprint, expectedContract?, leaseDuration?, retention?, ct)` returns an `IdempotentAdmission` whose `Disposition` is one of:

| Disposition | Meaning | What is set |
| --- | --- | --- |
| `Admitted` | The caller owns the operation under a fenced lease. | `Lease`, `LeaseExpiresAt`, `IsTakeover`, `Retention` |
| `InFlight` | A live attempt owns the key; retry later. | `LeaseExpiresAt` (the live holder's) |
| `Replay` | The operation already completed within retention. | `Result` (`IdempotentResult`: `Payload`, `Contract`) |
| `Conflict` | The key is stored with a different fingerprint, or a completed result carries a contract the caller did not expect. | `StoredFingerprint`, and `StoredContract` for a contract mismatch |

Under the hood, admission locks or inserts the key's record row, then decides in order: fingerprint mismatch → `Conflict`; `Completed` within retention → `Replay` (or contract `Conflict` if `expectedContract` does not match); otherwise it calls `unit.Leases.GrantAsync(kind: "headless.idempotency", resource: key)` — `Held` becomes `InFlight`, `Granted` or `Takeover` becomes `Admitted`. `IsTakeover` is set whenever the record was already `Pending` before this call, which covers both a crashed attempt and one a sweep abandoned.

### Fingerprint

`IdempotencyFingerprint` is a versioned digest of the request an idempotency key stands for: two admissions of one key must carry the same fingerprint, or the second is a `Conflict`. `IdempotencyFingerprint.V1` is SHA-256 over a caller-supplied canonical payload — the caller owns canonicalization (sorted fields, a fixed number format) so that two requests it considers equal produce identical bytes. `Compute(ReadOnlySpan<byte>)` and `Compute(string)` (UTF-8) compute it; `Matches(stored)` compares against a record's stored fingerprint and throws `NotSupportedException` for an unknown stored algorithm rather than guessing.

### Result and contract

`IdempotentResult` is opaque `Payload` bytes plus a `Contract` tag — the store never interprets them. `IdempotentOperationsJsonExtensions` adds typed helpers over both `IIdempotentOperations` and `UnitOfWorkIdempotency`: `CompleteAsync<T>(admission, result, JsonTypeInfo<T> typeInfo, contract, ...)` serializes to UTF-8 JSON before storing, and `IdempotentResult.Deserialize<T>(JsonTypeInfo<T>)` reads it back. Both take source-generated `JsonTypeInfo<T>` rather than reflection-based options, so they work under trimming and native AOT.

### Retention and purge

`IdempotentOperationsOptions.DefaultRetention` (24 hours) is how long a completed record replays, and `DefaultLeaseDuration` (2 minutes) is how long an admitted attempt owns its key unless renewed — both apply when the caller does not pass an explicit value to `AdmitAsync`, and the lease duration must also fall within the fencing bounds (`FencingOptions.MinimumLeaseDuration`/`MaximumLeaseDuration`). `IdempotencyRetentionService`, a hosted service always registered by `AddHeadlessIdempotency`, deletes records past `retention_until` in batches of `PurgeBatchSize` (default 1000) every `PurgeInterval` (default 1 hour; `null` disables the service entirely), then purges the idempotency-kind leases those records left behind through `IFencedLeases.PurgeAsync`. Records are purged before their leases, so a lease is never deleted while a record that might still complete under it survives.

### Enlistment and lock order

Enlisted calls (`unit.Idempotency.*`) follow the same rules as `unit.Leases` and `unit.Sequences` (see [unit-of-work.md](unit-of-work.md)): `RequireTransaction`, then `ValidateEnlistment`, then — for admit, complete, and release, never for the fence read — mark an observed-mode unit non-retryable. Every idempotency call, autonomous or enlisted, locks the key's record before its lease, in that fixed order; `unit.Idempotency.FenceAsync` preserves it (record, then lease) so it can never deadlock against a concurrent admission of the same key, which is why it exists instead of asking callers to fence the lease directly.

## HTTP composition

`Headless.Api.Idempotency` is a thin HTTP adapter over this store — see [api.md § Headless.Api.Idempotency](api.md#headlessapiidempotency) for its options and setup. What it adds on top of `IIdempotentOperations`:

- **Store key.** The SHA-256 hex of a derived scope string (user, method, path, query, header key) — always 64 characters, so a long path or a 255-character header value stays within `IdempotencyFieldLimits.KeyMaxLength`. The store separately scopes every key by the current tenant.
- **Admitted.** The middleware runs the handler behind a lease-renewal loop (`RenewAsync` every third of the configured lease duration, each call bounded by that same interval, since a handler's own fenced transaction can block a renewal on the lease row), then completes with the captured response once `ShouldCacheResponse` accepts it, or releases otherwise.
- **The post-commit completion window.** Completion runs *after* the handler's unit commits, not inside it — there is no enlisted HTTP completion today. A crash between the handler's commit and the middleware's completion call leaves the record `Pending`; the next request is `Admitted` as a takeover (`IsTakeover = true`) and re-runs the handler. A handler reads its admission with `HttpContext.GetIdempotencyContext()` (`IIdempotencyContext`) and either checks `IsTakeover` before repeating a side effect that is not safe to redo, or fences its own writes with `unit.Idempotency.FenceAsync(context.Admission)` so a still-running previous attempt (past its own takeover) cannot also commit.
- **Replay.** Writes the stored response verbatim (status, allowlisted headers, body). **Conflict.** Returns the configured mismatch status.
- **InFlight.** `Reject` (default): `409 g:idempotency_in_flight`. `WaitAndReplay`: each tick of a doubling backoff calls the cheap `PeekAsync` first and only re-runs the full, row-locking `AdmitAsync` when the peek shows the record settled or the holder's last-known lease expiry has passed — until `Replay`, `Admitted` (a takeover), or the configured timeout: `409 g:idempotency_in_flight_timeout`.

---

## Headless.Idempotency.Abstractions

Consumer contracts: `IIdempotentOperations`, `IdempotentAdmission`, `IdempotencyFingerprint`, `IdempotencyKey`, `IdempotentResult`, the JSON helpers, and the `unit.Idempotency` accessor.

### Setup

```bash
dotnet add package Headless.Idempotency.Abstractions
```

Reference it from code that admits, completes, or fences idempotent operations. Registration lives in `Headless.Idempotency.Core` and the provider packages.

### Design and runtime behavior

- `IIdempotentOperations.AdmitAsync(key, fingerprint, expectedContract?, leaseDuration?, retention?, ct)` → `IdempotentAdmission`. `CompleteAsync(admission, result, contract, retention?, ct)` stores the result and settles the lease in one transaction; throws `StaleLeaseException` when the attempt no longer owns the key. `ReleaseAsync(admission, ct)` → `LeaseSettlementStatus`. `RenewAsync(admission, duration, ct)` → `LeaseRenewalResult` (autonomous only). `PeekAsync(key, ct)` → `IdempotencyPeekStatus` (`Absent`, `Pending`, `Completed`; a record past its retention reads as `Absent`) — no row lock, no lease read, and no full disposition; it exists to poll cheaply while waiting on another attempt, not to replace `AdmitAsync`.
- `IdempotentAdmission`: `Disposition`, `Key` (`IdempotencyKey(TenantId, Key)`), `Fingerprint`, `Lease`/`LeaseExpiresAt` (when relevant), `IsTakeover`, `Retention`, `Result`, `StoredFingerprint`, `StoredContract`, and `IsAdmitted` (`[MemberNotNullWhen(true, nameof(Lease), nameof(Retention))]`). `IdempotentAdmission.LeaseKind` is the constant `"headless.idempotency"` every admission's lease is granted under.
- `unit.Idempotency` (namespace `Headless.UnitOfWork`) returns a `UnitOfWorkIdempotency` bound to the unit; repeated reads on one unit return the same instance. It mirrors `AdmitAsync`/`CompleteAsync`/`ReleaseAsync` plus `FenceAsync(admission, ct)`, which throws `StaleLeaseException` rather than returning a status. There is no enlisted `RenewAsync`.
- `unit.Idempotency` throws `InvalidOperationException` naming `AddHeadlessIdempotency` when no provider registered the feature.

---

## Headless.Idempotency.Core

Registration, fingerprint and key resolution, admission orchestration, and the retention purge.

### Setup

```bash
dotnet add package Headless.Idempotency.Core
```

Applications reach it through a provider package; call `AddHeadlessIdempotency` after `AddHeadlessFencing`, as shown in [Orientation](#orientation).

### Configuration

| Builder member | Effect |
| --- | --- |
| `ConfigureOptions(Action<IdempotentOperationsOptions>)` | Sets `DefaultRetention` (24 hours), `DefaultLeaseDuration` (2 minutes), `PurgeInterval` (1 hour; `null` disables the purge), `PurgeBatchSize` (1000) |
| `ConfigureStorage(Action<IdempotencyStorageOptions>)` / `ConfigureStorage(IConfiguration)` | Sets `Schema` (default `"idempotency"`), the schema the record table lives in |

### Design and runtime behavior

- `AddHeadlessIdempotency` throws `InvalidOperationException` at registration when no `IFencedLeases` is already registered, and requires exactly one `Use…` provider call.
- It registers `IIdempotentOperations`, `IUnitOfWorkIdempotency`, and `IdempotencyRequestResolver` as singletons, and always adds `IdempotencyRetentionService` as a hosted service — when `PurgeInterval` is `null` the service stays idle on a fixed one-minute re-check cadence instead of exiting, so a later reload back to a value resumes purging without restarting the host.
- `IIdempotencyRecordStore` is the provider seam. Applications do not call it.

---

## Headless.Idempotency.PostgreSql

PostgreSQL storage for idempotency records.

### Setup

```bash
dotnet add package Headless.Idempotency.PostgreSql
```

```csharp
builder.Services.AddHeadlessIdempotency(setup => setup.UsePostgreSql(connectionString)); // same database as AddHeadlessFencing
```

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | Must be the same database `AddHeadlessFencing` uses — every admission, completion, and release writes its record and its lease in one transaction |
| `CommandTimeout` | 30 seconds | Also bounds how long a concurrent admission of the same key waits behind an open enlisted admission, fence, or completion |
| `InitializeOnStartup` | `true` | When `false`, the application creates the schema, record table, and index |

### Design and runtime behavior

- Record insert-or-lock never raises a unique-violation: `INSERT … ON CONFLICT DO NOTHING` followed by `SELECT … FOR UPDATE`. Catching `23505` inside a unit would otherwise poison the PostgreSQL transaction (`25P02`).
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
builder.Services.AddHeadlessIdempotency(setup => setup.UseSqlServer(connectionString)); // same database as AddHeadlessFencing
```

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | Must be the same database `AddHeadlessFencing` uses — every admission, completion, and release writes its record and its lease in one transaction. Name the database explicitly (`Initial Catalog`) |
| `CommandTimeout` | 30 seconds | Also bounds how long a concurrent admission of the same key waits behind an open enlisted admission, fence, or completion |
| `InitializeOnStartup` | `true` | When `false`, the application creates the schema, record table, and index |

### Design and runtime behavior

- Record insert-or-lock never raises a unique-violation: `SELECT … WITH (UPDLOCK, HOLDLOCK)` then `IF NOT EXISTS INSERT`, with no `TRY/CATCH` — a caught duplicate key would doom an `XACT_ABORT` caller transaction.
- The autonomous path begins an owned unit through `IIdempotencyRecordStore.BeginOwnedUnitAsync` at READ COMMITTED; the enlisted path runs on the unit's own connection and transaction with no retry.
- The initializer serializes concurrent hosts with `sp_getapplock` and creates the schema and table idempotently.
