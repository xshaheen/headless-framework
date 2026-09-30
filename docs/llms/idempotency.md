---
domain: Idempotency
packages: Idempotency.Abstractions, Idempotency.Core, Idempotency.InMemory, Idempotency.Caching, Idempotency.PostgreSql, Idempotency.SqlServer
---

# Idempotency

> Durable, tenant-scoped idempotent admission: admit a key once across processes, replay its stored result on retry, and refuse a stale attempt's completion. Each record row carries its own lease and generation.

## Orientation

Register one provider; nothing else is required:

```csharp
builder.Services.AddHeadlessIdempotency(setup => setup.UsePostgreSql(connectionString)); // or setup.UseSqlServer(...)
// tests, local development, one instance: builder.Services.AddHeadlessIdempotency(setup => setup.UseInMemory());
// several replicas sharing Redis, no SQL database, autonomous calls only: setup.UseCache()
```

Each key's record row holds the admitted attempt's lease itself (a generation plus a lease expiry), the same shape Stripe idempotency keys and AWS Powertools idempotency use. There is no second table, so every call locks exactly one row, and there is no lock order to get wrong. Idempotency does not depend on [`Headless.Fencing`](fencing.md).

- **Autonomous** — inject `IIdempotentOperations` and call `AdmitAsync`, `CompleteAsync`, `SetRecoveryPointAsync`, `ReleaseAsync`, `RenewAsync`, or `PeekAsync`. Admission and completion each commit before they return; `PeekAsync` is a cheap, lock-free status read with no transaction of its own.
- **Enlisted** — `unit.Idempotency` (namespace `Headless.UnitOfWork`, added by `Headless.Idempotency.Abstractions`) runs `AdmitAsync`, `CompleteAsync`, `SetRecoveryPointAsync`, `ReleaseAsync`, and `FenceAsync` inside the caller's own transaction, so a rollback leaves no record.

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
- `IsTakeover` means an earlier attempt was admitted for this key and ended without completing or releasing — it crashed or stalled past its lease. Its partial side effects may already exist. An operation that is not naturally safe to repeat must check `IsTakeover` before redoing them, or record [recovery points](#recovery-points) and resume from `admission.RecoveryPoint`.
- `CompleteAsync` and `ReleaseAsync` both take the whole `IdempotentAdmission`, not just the key — the admission carries the `Generation` the completion is fenced against. A `StaleAdmissionException` from `CompleteAsync` means the attempt no longer owns the key (`Reason`: `Expired`, `Stale` after a takeover or purge, `Completed` for a second completion by the same attempt, or `Released`); nothing was stored, and a stored result is never overwritten.
- A stored fingerprint from an algorithm this version does not know (`IdempotencyFingerprint.IsKnownAlgorithm` is `false`) is refused with `NotSupportedException`, never recomputed — the request that produced it is gone, so guessing could replay a wrong result or reject a legitimate retry.
- `expectedContract` on `AdmitAsync` is a read guard, not a write guard: a completed record stored under a different contract tag comes back as `Conflict` (`StoredContract` set) rather than a mis-typed replay. Pass the same contract tag to `AdmitAsync` and `CompleteAsync`.
- `RenewAsync` is one guarded single-row update the store commits on its own connection — call it from a heartbeat loop without an owned unit. It extends the lease only while the record is pending at the admission's generation with a live lease, and otherwise reports why (`IdempotentLeaseRenewal.Status`). It is the one autonomous-only member; there is no `unit.Idempotency.RenewAsync`.
- The idempotency key, tenant id, and contract pass through `IdempotencyFieldLimits` (`TenantIdMaxLength` 128, `KeyMaxLength` 256, `ContractMaxLength` 256, `FingerprintAlgorithmMaxLength` 32, `FingerprintMaxLength` 64, `RecoveryPointMaxLength` 128, `RecoveryStateMaxLength` 64 KiB) before any SQL runs, and a lease duration must fall within `IdempotentOperationsOptions.MinimumLeaseDuration`/`MaximumLeaseDuration`.

## How this differs from locks and fenced leases

Idempotent admission answers "has this operation already happened, and what was its result?". It admits one attempt per tenant-scoped key and fingerprint and replays the stored result to every later retry. It does not serialize different keys over a shared resource: use a [distributed lock](distributed-locks.md) for "one process at a time", and a [fenced lease](fencing.md) for ownership of work with no result to replay, such as a resource handed to an external executor. Comparison of all four primitives: [Choosing a coordination primitive](fencing.md#choosing-a-coordination-primitive).

## Core Concepts

### Admission

`AdmitAsync(key, fingerprint, expectedContract?, leaseDuration?, retention?, ct)` returns an `IdempotentAdmission` whose `Disposition` is one of:

| Disposition | Meaning | What is set |
| --- | --- | --- |
| `Admitted` | The caller owns the operation under a lease on its record. | `Generation`, `LeaseExpiresAt`, `IsTakeover`, `Retention`, and `RecoveryPoint` when an earlier attempt recorded one |
| `InFlight` | A live attempt owns the key; retry later. | `Generation` and `LeaseExpiresAt` (the live holder's) |
| `Replay` | The operation already completed within retention. | `Result` (`IdempotentResult`: `Payload`, `Contract`) |
| `Conflict` | The key is stored with a different fingerprint, or a completed result carries a contract the caller did not expect. | `StoredFingerprint`, and `StoredContract` for a contract mismatch |

Under the hood, admission locks or inserts the key's record row (one row, one lock), reads the database clock after the lock is held, then decides in order: fingerprint mismatch → `Conflict`; `Completed` within retention → `Replay` (or contract `Conflict` if `expectedContract` does not match); `Pending` with a generation whose lease is still live → `InFlight`; otherwise it grants: draws the next generation from the store-wide generation sequence, sets the lease expiry to the database clock plus the lease duration, and returns `Admitted`. A record past its retention is reset in place and granted, unless a live attempt still holds it (its lease was renewed past the retention). `IsTakeover` is set when the record was `Pending` with a generation before this call, meaning an attempt ended without completing or releasing.

Generations only grow per key, even after the record is purged and the key admitted again, because they come from one sequence, drawn only after the row lock is held. A zombie attempt of a purged record can never match a new one.

### Fingerprint

`IdempotencyFingerprint` is a versioned digest of the request an idempotency key stands for: two admissions of one key must carry the same fingerprint, or the second is a `Conflict`. `IdempotencyFingerprint.V1` is SHA-256 over a caller-supplied canonical payload — the caller owns canonicalization (sorted fields, a fixed number format) so that two requests it considers equal produce identical bytes. `Compute(ReadOnlySpan<byte>)` and `Compute(string)` (UTF-8) compute it; `Matches(stored)` compares against a record's stored fingerprint and throws `NotSupportedException` for an unknown stored algorithm rather than guessing.

### Result and contract

`IdempotentResult` is opaque `Payload` bytes plus a `Contract` tag — the store never interprets them. `IdempotentOperationsJsonExtensions` adds typed helpers over both `IIdempotentOperations` and `UnitOfWorkIdempotency`: `CompleteAsync<T>(admission, result, JsonTypeInfo<T> typeInfo, contract, ...)` serializes to UTF-8 JSON before storing, and `IdempotentResult.Deserialize<T>(JsonTypeInfo<T>)` reads it back. Both take source-generated `JsonTypeInfo<T>` rather than reflection-based options, so they work under trimming and native AOT.

### Retention and purge

`IdempotentOperationsOptions.DefaultRetention` (24 hours) is how long a completed record replays, and `DefaultLeaseDuration` (2 minutes) is how long an admitted attempt owns its key unless renewed — both apply when the caller does not pass an explicit value to `AdmitAsync`. Every admission and renewal duration must fall within `MinimumLeaseDuration` (1 second) and `MaximumLeaseDuration` (1 day). `IdempotencyRetentionService`, a hosted service always registered by `AddHeadlessIdempotency`, deletes records past `retention_until` whose lease is no longer live, in batches of `PurgeBatchSize` (default 1000) every `PurgeInterval` (default 1 hour; `null` disables the service entirely). A pending record whose attempt renewed its lease past the retention is kept until that lease expires, so a live attempt never loses the record it will complete.

### Recovery points

A recovery point is the last step an admitted attempt recorded as done, plus the state it needs to resume after that step: a short `Name`, opaque `State` bytes, and a `Contract` tag (`IdempotentRecoveryPoint`). The record keeps one. Use recovery points when an operation has several steps whose effects must not repeat, such as a payment API call followed by an email, or when a step's effect commits before the attempt's completion does.

- **Record** after each step with `unit.Idempotency.SetRecoveryPointAsync(admission, point, state, contract, ct)`, in the same unit as the step's own writes, so the point commits or rolls back with them. `IIdempotentOperations.SetRecoveryPointAsync` is the autonomous form for a step whose effect lives outside any database the unit could join; it commits before it returns. The typed helper `SetRecoveryPointAsync<T>(admission, point, state, JsonTypeInfo<T>, contract, ct)` serializes the state to UTF-8 JSON, and `IdempotentRecoveryPoint.Deserialize<T>(JsonTypeInfo<T>)` reads it back.
- **Fenced like a completion.** The call takes the record row's update-intent lock, then writes only while the record is pending at the admission's generation with a live lease by the database clock. Otherwise it throws `StaleAdmissionException` and writes nothing, because an attempt that lost the key must not tell its successor where to resume. The name must be non-blank and at most `RecoveryPointMaxLength` (128) characters, the contract follows the result-contract rules, and the state is at most `RecoveryStateMaxLength` (64 KiB). An invalid or oversized point is refused before any command runs. Keep larger resume data in the operation's own storage and let the point say where it is.
- **Resume.** The next admission of the key hands the point back on `IdempotentAdmission.RecoveryPoint`. That happens both after a takeover (`IsTakeover`, the earlier attempt crashed or stalled) and after a release (the earlier attempt gave the key up). Skip the steps up to and including the named one.
- **What ends it.** A later `SetRecoveryPointAsync` replaces the point. Completion clears it, because a replay needs only the result. A release keeps it: the released attempt may still have finished steps that must not repeat, and the middleware releases on a handler exception, which is when resuming matters most. A record reset after its retention elapsed drops it, because that admission is a new operation that may carry another request's fingerprint. A first admission never has one.

**The "done" point.** Record a final point in the same unit as the operation's last writes and put the result in its state. If the attempt dies after that unit commits but before its completion does, the retry takes the key over, finds the "done" point, and returns the stored result without running the work again:

```csharp
if (admission.RecoveryPoint is { Name: "done" } done)
{
    return done.Deserialize(ReceiptJsonContext.Default.Receipt); // the work already committed
}

return await factory.RunAsync(db, async (unit, ct) =>
{
    var receipt = await ChargeAsync(unit, ct);
    await unit.Idempotency.SetRecoveryPointAsync(admission, "done", receipt, ReceiptJsonContext.Default.Receipt, "orders.receipt/v1", ct);
    return receipt;
}, cancellationToken: ct);
```

### Enlistment and the fence

Enlisted calls (`unit.Idempotency.*`) follow the same rules as `unit.Leases` and `unit.Sequences` (see [unit-of-work.md](unit-of-work.md)): `RequireTransaction`, then `ValidateEnlistment`, then — for admit, complete, set-recovery-point, and release, never for the fence read — mark an observed-mode unit non-retryable. What the unit must carry is the provider's judgment: the relational providers need a live transaction on their own database, while the in-memory provider refuses any unit over a database connection and accepts a resource-less unit (`IUnitOfWorkFactory.BeginAsync()`), which then plays the transaction. The cache provider refuses every caller unit, because a cache cannot commit or roll back with one. `unit.Idempotency.FenceAsync` takes the record row with an update-intent lock (`FOR NO KEY UPDATE` on PostgreSQL, `UPDLOCK, HOLDLOCK, ROWLOCK` on SQL Server) held until the unit ends, then refuses with `StaleAdmissionException` unless the record is still pending at the admission's generation with a live lease by the database clock read after the lock. A concurrent admission of the key waits on that row and then sees the unit's committed outcome. The lock is never shared: a waiting admission would queue for the update lock behind it, and the fencing unit's own completion would then deadlock.

## HTTP composition

`Headless.Api.Idempotency` is a thin HTTP adapter over this store — see [api.md § Headless.Api.Idempotency](api.md#headlessapiidempotency) for its options and setup. What it adds on top of `IIdempotentOperations`:

- **Store key.** The SHA-256 hex of a derived scope string (user, method, path, query, header key) — always 64 characters, so a long path or a 255-character header value stays within `IdempotencyFieldLimits.KeyMaxLength`.
- **Store tenant.** The middleware admits and peeks under the authenticated principal's tenant claim (the claim type `MultiTenancyOptions.ClaimType` names, `tenant_id` by default), switching `ICurrentTenant` only for those store calls; the handler still runs under the ambient tenant. It never keys by the ambient tenant, because pre-auth catalog resolution sets that from a host or header the caller controls, which would let an anonymous caller choose a tenant's namespace to pre-seed or replay. A principal without a tenant claim, and every anonymous request, keys under the host scope. A `KeyDeriver` changes only the scope string, never the store tenant.
- **Anonymous endpoints.** With `RequireUserIdentity = false`, every anonymous request shares one namespace across all tenants: the same key on the same method, path, and query replays across tenants routed by host or header, and any caller can pre-seed a key an anonymous sender will use later. Give webhook receivers and similar endpoints a `KeyDeriver` whose scope carries a verified per-sender discriminator (for example the account a signature was verified for). Claim-less authenticated principals likewise share the host scope, so their records stay apart only while user ids are unique across tenants.
- **Admitted.** The middleware runs the handler behind a lease-renewal loop (`RenewAsync` every third of the configured lease duration, each call bounded by that same interval, since a handler's own fenced transaction can block a renewal on the record row), then completes with the captured response once `ShouldCacheResponse` accepts it, or releases otherwise.
- **The post-commit completion window.** Completion runs *after* the handler's unit commits, not inside it. A crash between the handler's commit and the middleware's completion call leaves the record `Pending`; the next request is `Admitted` as a takeover (`IsTakeover = true`) and runs the handler again. A handler reads its admission with `HttpContext.GetIdempotencyContext()` (`IIdempotencyContext`). To close the window, it records a ["done" recovery point](#recovery-points) carrying its response state in the same unit as its writes, with `unit.Idempotency.SetRecoveryPointAsync(context.Admission, "done", state, contract)`. On the retry, it finds `context.RecoveryPoint` and rebuilds the response from that state instead of repeating the work. It can also fence its own writes with `unit.Idempotency.FenceAsync(context.Admission)`, so a previous attempt that is still running after its own takeover cannot also commit.
- **Replay.** Writes the stored response verbatim (status, allowlisted headers, body). **Conflict.** Returns the configured mismatch status.
- **InFlight.** `Reject` (default): `409 g:idempotency_in_flight`. `WaitAndReplay`: each tick of a doubling backoff calls the cheap `PeekAsync` first and only re-runs the full, row-locking `AdmitAsync` when the peek shows the record settled or the holder's last-known lease expiry has passed — until `Replay`, `Admitted` (a takeover), or the configured timeout: `409 g:idempotency_in_flight_timeout`.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Idempotency.PostgreSql` | Records must survive restarts and be shared by every instance, and enlisted units run on PostgreSQL | Units run on SQL Server | Insert-or-lock with `ON CONFLICT DO NOTHING`; `clock_timestamp()` decides |
| `Headless.Idempotency.SqlServer` | The same, with units on SQL Server | — | `UPDLOCK, HOLDLOCK` insert-or-lock without `TRY/CATCH`; `SYSUTCDATETIME()` decides |
| `Headless.Idempotency.InMemory` | Tests, local development, or a single-instance host with no relational database | Several instances serve the same keys, or retries must be deduplicated across a restart | Records live in one process and vanish on restart; the registered `TimeProvider` decides leases and retention; enlisted calls run only on resource-less units, so a fence cannot join a database transaction |
| `Headless.Idempotency.Caching` | Several replicas share a Redis cache and there is no SQL database, and every call is autonomous (`IIdempotentOperations`, the HTTP middleware) | A result must survive a cache flush or eviction, or the operation needs `unit.Idempotency`, `FenceAsync`, or a recovery point recorded inside a unit | Autonomous only; durability is the cache's, so an evicted or flushed record means the operation runs again; the application clock decides leases, so replica clock skew shifts expiry; completion is still compare-and-swap guarded by generation, so a zombie never overwrites a newer result |

---

## Headless.Idempotency.Abstractions

Consumer contracts: `IIdempotentOperations`, `IdempotentAdmission`, `IdempotencyFingerprint`, `IdempotencyKey`, `IdempotentResult`, the JSON helpers, and the `unit.Idempotency` accessor.

### Setup

```bash
dotnet add package Headless.Idempotency.Abstractions
```

Reference it from code that admits, completes, or fences idempotent operations. Registration lives in `Headless.Idempotency.Core` and the provider packages.

### Design and runtime behavior

- `IIdempotentOperations.AdmitAsync(key, fingerprint, expectedContract?, leaseDuration?, retention?, ct)` → `IdempotentAdmission`. `CompleteAsync(admission, result, contract, retention?, ct)` stores the result and ends the lease in one transaction; throws `StaleAdmissionException` when the attempt no longer owns the key. `ReleaseAsync(admission, ct)` → `IdempotentLeaseStatus` (`Released` on success or when the key is already released; `Expired`, `Stale`, or `Completed` for a refusal that wrote nothing). `SetRecoveryPointAsync(admission, point, state, contract, ct)` records the attempt's last finished step in its own committed transaction, fenced like a completion. `RenewAsync(admission, duration, ct)` → `IdempotentLeaseRenewal(Status, ExpiresAt)`, with `IsRenewed` when `Status` is `Current` (autonomous only). `PeekAsync(key, ct)` → `IdempotencyPeekStatus` (`Absent`, `Pending`, `Completed`; a record past its retention reads as `Absent`) — no row lock, no lease read, and no full disposition; it exists to poll cheaply while waiting on another attempt, not to replace `AdmitAsync`.
- `IdempotentAdmission`: `Disposition`, `Key` (`IdempotencyKey(TenantId, Key)`), `Fingerprint`, `Generation`/`LeaseExpiresAt` (when relevant), `IsTakeover`, `Retention`, `RecoveryPoint` (`IdempotentRecoveryPoint`: `Name`, `State`, `Contract`), `Result`, `StoredFingerprint`, `StoredContract`, and `IsAdmitted` (`[MemberNotNullWhen(true, nameof(Generation), nameof(LeaseExpiresAt), nameof(Retention))]`).
- `IdempotentLeaseStatus` is what the store found for an attempt's generation: `Current`, `Expired`, `Stale`, `Completed`, or `Released`. `StaleAdmissionException` (an `InvalidOperationException`) carries the refused `Key`, `Generation`, and `Reason`.
- `unit.Idempotency` (namespace `Headless.UnitOfWork`) returns a `UnitOfWorkIdempotency` bound to the unit; repeated reads on one unit return the same instance. It mirrors `AdmitAsync`/`CompleteAsync`/`SetRecoveryPointAsync`/`ReleaseAsync` plus `FenceAsync(admission, ct)`, which throws `StaleAdmissionException` rather than returning a status. There is no enlisted `RenewAsync`.
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
| `ConfigureStorage(Action<IdempotencyStorageOptions>)` / `ConfigureStorage(IConfiguration)` | Sets `Schema` (default `"headless"`, the schema every Headless feature shares), the schema the record table and its generation sequence live in (`idempotency_records` and `idempotency_record_generations` on PostgreSQL, `IdempotencyRecords` and `IdempotencyRecordGenerations` on SQL Server) |

### Design and runtime behavior

- `AddHeadlessIdempotency` requires exactly one `Use…` provider call and nothing else. It swaps the `NullCurrentTenant` fallback for the `AsyncLocal`-backed `CurrentTenant` unless the host registered its own, because records are keyed by the current tenant.
- It registers `IIdempotentOperations`, `IUnitOfWorkIdempotency`, and `IdempotencyRequestResolver` as singletons, and always adds `IdempotencyRetentionService` as a hosted service — when `PurgeInterval` is `null` the service stays idle on a fixed one-minute re-check cadence instead of exiting, so a later reload back to a value resumes purging without restarting the host.
- `IIdempotencyRecordStore` is the provider seam. Applications do not call it.
- An optimistic provider (one that checks at write time instead of locking at read time, such as the cache provider) throws `IdempotencyRecordConflictException` from a write another caller beat. The autonomous `AdmitAsync`, `CompleteAsync`, `SetRecoveryPointAsync`, and `ReleaseAsync` then run again, whole, in a fresh owned unit, at most 8 attempts with no delay between them. The locking providers never throw it.

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
- Every admission, fence, completion, recovery point, and release takes an exclusive per-key lock, so the admission decision table (conflict, replay, in flight, takeover, reset past retention) runs on one consistent record, exactly as on the relational providers. Generations come from one process-wide counter, so they grow per key across releases, takeovers, and purges, but restart from 1 when the process restarts, when every earlier record is gone too.
- Autonomous calls run in a resource-less owned unit the store begins. Enlisted calls (`unit.Idempotency`) run only on a resource-less unit (`IUnitOfWorkFactory.BeginAsync()`); a unit over a database connection or `DbContext` is refused with `InvalidOperationException`, because in-memory records cannot commit or roll back with that transaction. The unit is the commit boundary: the record's lock is held until the unit ends, its writes reach the table only when the unit completes, and a rollback or a dispose without completing drops them. One unit may fence and then complete the same record without waiting on itself.
- `FenceAsync` holds the record until the unit ends, so no admission in this process can take the key over under the unit. It guards nothing outside the process and is not coupled to any database transaction.
- `PeekAsync` reads the committed record without the lock and never sees a unit's uncommitted writes. `RenewAsync` takes the lock and commits at once.
- There is no deadlock detection. Units that lock several keys must lock them in one consistent order, and callers should pass a cancellation token that bounds the wait.
- The retention purge skips a record a unit holds and keeps a record whose lease is still live, like the relational providers.
- `Headless.Api.Idempotency` works on it unchanged: `AddHeadlessIdempotency(setup => setup.UseInMemory())` plus `AddIdempotency(...)` needs no database.

---

## Headless.Idempotency.Caching

Idempotency records in the application's `ICache`, for several replicas that share a Redis cache and have no SQL database. Autonomous calls only.

### Setup

```bash
dotnet add package Headless.Idempotency.Caching
```

```csharp
builder.Services.AddHeadlessCaching(setup => setup.UseRedis(options => options.ConnectionMultiplexer = redis));
builder.Services.AddHeadlessIdempotency(setup => setup.UseCache());
```

`UseCache()` needs a caching provider registered through `AddHeadlessCaching`; host startup fails without one. It also has `UseCache(Action<CacheIdempotencyOptions>)`, `UseCache(Action<CacheIdempotencyOptions, IServiceProvider>)`, and `UseCache(IConfiguration)`. It registers `TimeProvider.System` and the unit-of-work factory when the host has not. `ConfigureStorage` has no effect on it.

### Configuration

| `CacheIdempotencyOptions` | Default | Effect |
| --- | --- | --- |
| `KeyPrefix` | `headless:idempotency:` | Prefix of every entry: `{prefix}record:{tenant length}:{tenant}{key}` per record and `{prefix}generation` for the counter. Give applications that share a cache distinct prefixes. |
| `CacheName` | `null` | A keyed `ICache` instance to hold the records. `null` uses the registered `IRemoteCache` when there is one, otherwise the default `ICache`. Name a remote (Redis) instance, not a hybrid one. |

### Design and runtime behavior

- **Autonomous only.** `IIdempotentOperations` and the HTTP middleware work. Every `unit.Idempotency` call is refused with `InvalidOperationException`: that includes enlisted admission, completion, and release, `FenceAsync`, and `SetRecoveryPointAsync` inside a unit. A cache cannot commit or roll back with a unit of work, so use a relational provider (or `UseInMemory` in one process) for those. The autonomous `SetRecoveryPointAsync` works.
- **One entry per record.** Each key's record is one cache entry holding the whole record as JSON (status, fingerprint and algorithm, generation, lease expiry, result bytes and contract, recovery point, retention). Every change is a compare-and-swap against the exact entry the call read: `TryInsertAsync` for a new record, `TryReplaceIfEqualAsync` otherwise. A call that loses the race re-reads and decides again (see [Headless.Idempotency.Core](#headlessidempotencycore)), so concurrent admissions converge like locked ones: one `Admitted`, the others `InFlight`, `Replay`, or `Conflict`. A write the cache declines while the entry is unchanged (for example an entry over an in-memory cache's size limit) throws `InvalidOperationException` instead of retrying.
- **Completion is fenced by generation.** Complete, release, recovery point, and renewal each write only while the entry is pending at the attempt's generation with a live lease, and the swap fails if anything changed since that check. An attempt that lost the key gets `StaleAdmissionException` and writes nothing, so a zombie never overwrites a newer result. There is no lock between the check and the swap, so a slow attempt's write races a takeover through the swap rather than waiting on a row lock.
- **Application clock.** Lease expiry and retention are instants from the registered `TimeProvider`, stored in the entry. Replicas whose clocks disagree disagree on when a lease expires: a replica running ahead takes over a live attempt early. Keep lease durations well above the expected skew.
- **Retention is the cache's expiry.** Each entry expires in the cache at the later of its retention and its lease, so a live attempt keeps its record even past retention. `PurgeAsync` deletes nothing and `IdempotencyRetentionService` has nothing to do; set `PurgeInterval = null` to keep it idle.
- **Durability is the cache's.** An evicted or flushed record is a record that never existed: the next admission of the key is a fresh `Admitted`, not a `Replay`, and the operation runs again. Run Redis with a no-eviction policy (or `volatile-*` with enough memory) and persistence if that matters.
- **Generations.** Drawn from a cache counter (`IncrementAsync`) that never expires. A generation is never issued at or below the highest one the record ever admitted, and the counter is raised past it, so generations keep growing per key even if the counter is evicted. Losing the counter and the record together can restart generations from 1.
- `PeekAsync` is one cache read. `RenewAsync` is its own compare-and-swap loop with the same bound as the autonomous calls.
- `Headless.Api.Idempotency` works on it unchanged, including `WaitAndReplay`, since the middleware uses only the autonomous surface. A handler that calls `unit.Idempotency.FenceAsync` or records a recovery point inside its unit is refused.

---

## Headless.Idempotency.PostgreSql

PostgreSQL storage for idempotency records.

### Setup

```bash
dotnet add package Headless.Idempotency.PostgreSql
```

```csharp
builder.Services.AddHeadlessIdempotency(setup => setup.UsePostgreSql(connectionString));
// or reuse the connection from services.AddPostgreSqlSql(connectionString): setup.UsePostgreSql();
```

The parameterless overloads and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database that holds the records; an enlisted call is accepted only on a unit whose connection reaches it |
| `CommandTimeout` | 30 seconds | Also bounds how long a concurrent admission of the same key waits behind an open enlisted admission, fence, or completion |
| `InitializeOnStartup` | `true` | When `false`, the application creates the schema, the `idempotency_record_generations` sequence, the `idempotency_records` table, and its index |

### Design and runtime behavior

- Record insert-or-lock never raises a unique-violation: `INSERT … ON CONFLICT DO NOTHING` followed by `SELECT … FOR NO KEY UPDATE`. Catching `23505` inside a unit would otherwise poison the PostgreSQL transaction (`25P02`).
- Every decision reads `clock_timestamp()` after the row lock, captured once per statement in a `MATERIALIZED` CTE — never `now()`, which is frozen at transaction start and would keep an expired lease looking live inside a long enlisted unit. `nextval()` runs only in a statement after the lock is held.
- The autonomous path begins an owned unit through `IIdempotencyRecordStore.BeginOwnedUnitAsync` at READ COMMITTED; the enlisted path runs on the unit's own connection and transaction with no retry.
- Autonomous renewal and purge run in their own READ COMMITTED transaction and retry a deadlock or serialization failure (`40P01`, `40001`) in a fresh transaction up to 3 attempts, waiting a jittered delay (`n × 10–50 ms` before retry `n`, on the registered `TimeProvider`) between them. Enlisted calls never retry.
- The sequence, table, and index are one step the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts) applies at startup, under one advisory lock per database shared with every other Headless feature, recorded as `Idempotency/1`.

---

## Headless.Idempotency.SqlServer

SQL Server storage for idempotency records.

### Setup

```bash
dotnet add package Headless.Idempotency.SqlServer
```

```csharp
builder.Services.AddHeadlessIdempotency(setup => setup.UseSqlServer(connectionString));
// or reuse the connection from services.AddSqlServerSql(connectionString): setup.UseSqlServer();
```

The parameterless overloads and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database that holds the records; an enlisted call is accepted only on a unit whose connection reaches it. Name the database explicitly (`Initial Catalog`) |
| `CommandTimeout` | 30 seconds | Also bounds how long a concurrent admission of the same key waits behind an open enlisted admission, fence, or completion |
| `InitializeOnStartup` | `true` | When `false`, the application creates the schema, the `IdempotencyRecordGenerations` sequence, the `IdempotencyRecords` table, and its index |

### Design and runtime behavior

- Record insert-or-lock never raises a unique-violation: `SELECT … WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` then `IF NOT EXISTS INSERT`, with no `TRY/CATCH` — a caught duplicate key would doom an `XACT_ABORT` caller transaction.
- Each batch captures `SYSUTCDATETIME()` into a variable after the locking read. A generation is drawn with `SET @g = NEXT VALUE FOR …` into a variable after the lock, because `NEXT VALUE FOR` is illegal inside `CASE`, `OUTPUT`, `WHERE`, subqueries, and `MERGE`.
- The autonomous path begins an owned unit through `IIdempotencyRecordStore.BeginOwnedUnitAsync` at READ COMMITTED; the enlisted path runs on the unit's own connection and transaction with no retry.
- Autonomous renewal and purge run in their own READ COMMITTED transaction and retry a deadlock or snapshot update conflict (1205, 3960) in a fresh transaction up to 3 attempts, waiting a jittered delay (`n × 10–50 ms` before retry `n`, on the registered `TimeProvider`) between them. Enlisted calls never retry.
- The sequence, table, and index are one step the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts) applies at startup, under one `sp_getapplock` per database shared with every other Headless feature, recorded as `Idempotency/1`.
