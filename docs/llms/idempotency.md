---
domain: Idempotency
packages: Idempotency.Abstractions, Idempotency, Idempotency.InMemory, Idempotency.Caching, Idempotency.PostgreSql, Idempotency.SqlServer, Idempotency.Sqlite, Idempotency.ResponseReplay
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

Each key's record row holds the admitted attempt's lease itself (a generation plus a lease expiry). Every call locks exactly one row, so there is no lock order to get wrong. You do not register Fencing for it.

- **Autonomous**: inject `IIdempotentOperations` and call `AdmitAsync`, `CompleteAsync`, `SetRecoveryPointAsync`, `ReleaseAsync`, `RenewAsync`, `PeekAsync`, `GetResultAsync`, `InspectAsync`, or `Describe`. Admission and completion each commit before they return; `PeekAsync` is a cheap status read that takes no row lock, though on SQL Server without read committed snapshot isolation it can still wait on a writer (see [Headless.Idempotency.SqlServer](#headlessidempotencysqlserver)).
- **Enlisted**: `unit.Idempotency` (namespace `Headless.UnitOfWork`, added by `Headless.Idempotency.Abstractions`) runs `AdmitAsync`, `CompleteAsync`, `SetRecoveryPointAsync`, `ReleaseAsync`, and `FenceAsync` inside the caller's own transaction, so a rollback leaves no record.

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

**Verify:** on a relational provider, `dotnet MyApp.dll resources provision -k Schema` reports `Ok` for the `<Provider>/headless/Idempotency` resource, and a host started before that fails naming it. In a test, a second `AdmitAsync` with the same key and fingerprint after `CompleteAsync` answers `Replay`, and one with a different fingerprint answers `Conflict`.

`Headless.Idempotency.ResponseReplay` is the HTTP composition of this same store; see [§ HTTP composition](#http-composition) and [§ Headless.Idempotency.ResponseReplay](#headlessidempotencyresponsereplay).

## Agent Rules

- Fence an admitted operation's writes through `unit.Idempotency.FenceAsync(admission)` as the unit's first idempotency call. It takes the record row's update-intent lock (never a shared one) and holds it until the unit ends, so no admission can take the key over while the unit's writes are uncommitted.
- `IsTakeover` means an earlier attempt was admitted for this key and ended without completing or releasing: it crashed or stalled past its lease. Its partial side effects may already exist. An operation that is not naturally safe to repeat must check `IsTakeover` before redoing them, or record [recovery points](#recovery-points) and resume from `admission.RecoveryPoint`.
- `CompleteAsync` and `ReleaseAsync` both take the whole `IdempotentAdmission`, not just the key. The admission carries the `Generation` the completion is fenced against. A `StaleAdmissionException` from `CompleteAsync` means the attempt no longer owns the key (`Reason`: `Expired`, `Stale` after a takeover or purge, `Completed` for a second completion by the same attempt, or `Released`); nothing was stored, and a stored result is never overwritten.
- A stored fingerprint from an algorithm this version does not know (`IdempotencyFingerprint.IsKnownAlgorithm` is `false`) is refused with `NotSupportedException`, never recomputed, because the request that produced it is gone.
- `expectedContract` on `AdmitAsync` is a read guard, not a write guard: a completed record stored under a different contract tag comes back as `Conflict` (`StoredContract` set) rather than a mis-typed replay. Pass the same contract tag to `AdmitAsync` and `CompleteAsync`.
- `RenewAsync` is one guarded single-row update the store commits on its own connection. Call it from a heartbeat loop without an owned unit. It extends the lease only while the record is pending at the admission's generation with a live lease, and otherwise reports why (`IdempotentLeaseRenewal.Status`). The status describes the caller's own attempt: an attempt whose key a newer attempt took over and then completed or released answers `Stale`, not that newer attempt's `Completed` or `Released` (the cache provider answers `Released` there). It is the one autonomous-only member; there is no `unit.Idempotency.RenewAsync`.
- The idempotency key, tenant id, and contract pass through `IdempotencyFieldLimits` (`TenantIdMaxLength` 128, `KeyMaxLength` 256, `ContractMaxLength` 256, `FingerprintAlgorithmMaxLength` 32, `FingerprintMaxLength` 64, `RecoveryPointMaxLength` 128, `RecoveryStateMaxLength` 64 KiB) before any SQL runs, and a lease duration must fall within `IdempotentOperationsOptions.MinimumLeaseDuration`/`MaximumLeaseDuration`. Lease durations and retentions are then applied truncated to whole microseconds, the finest resolution every provider stores, so a lease or retention ends at the same instant whichever provider holds the record; a retention under one microsecond is refused.
- To show an operator one key (a dashboard lookup, a support tool), call `InspectAsync(key, ct)`, not `GetResultAsync`. `InspectAsync` returns the record's metadata and the result's size and contract without its bytes; `GetResultAsync` returns the stored body, which can hold personal data. Neither admits an attempt. No API releases, expires, or deletes a record for an operator: releasing a live attempt could run its operation twice.
- The key, tenant id, contract, and recovery point name must be text every provider keeps unchanged, on every provider including in-memory and cache. Surrounding white space, a NUL character, or an unpaired UTF-16 surrogate throws `ArgumentException` before any write.

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

Admission locks or inserts the key's record row, reads the database clock after the lock is held, and decides in this order:

1. Fingerprint mismatch → `Conflict`.
2. `Completed` within retention → `Replay`, or `Conflict` when `expectedContract` does not match.
3. `Pending` with a live lease → `InFlight`.
4. Otherwise → `Admitted` with a new generation and a lease expiry of the database clock plus the lease duration.

A record past its retention is reset in place and admitted, unless a live attempt still holds it (its lease was renewed past the retention). `IsTakeover` is set when the record was `Pending` with a generation before this call.

Generations only grow per key, even after the record is purged and the key admitted again, so a zombie attempt of a purged record can never match a new one.

### Fingerprint

`IdempotencyFingerprint` is a versioned digest of the request an idempotency key stands for: two admissions of one key must carry the same fingerprint, or the second is a `Conflict`. `IdempotencyFingerprint.V1` is SHA-256 over a caller-supplied canonical payload. The caller owns canonicalization (sorted fields, a fixed number format) so that two requests it considers equal produce identical bytes. `Compute(ReadOnlySpan<byte>)` and `Compute(string)` (UTF-8) compute it; `Matches(stored)` compares against a record's stored fingerprint and throws `NotSupportedException` for an unknown stored algorithm rather than guessing.

### Result and contract

`IdempotentResult` is opaque `Payload` bytes plus a `Contract` tag. The store never interprets them. `IdempotentOperationsJsonExtensions` adds typed helpers over both `IIdempotentOperations` and `UnitOfWorkIdempotency`: `CompleteAsync<T>(admission, result, JsonTypeInfo<T> typeInfo, contract, ...)` serializes to UTF-8 JSON before storing, and `IdempotentResult.Deserialize<T>(JsonTypeInfo<T>)` reads it back. Both take source-generated `JsonTypeInfo<T>` rather than reflection-based options, so they work under trimming and native AOT.

### Retention and purge

`IdempotentOperationsOptions.DefaultRetention` (24 hours) is how long a completed record replays, and `DefaultLeaseDuration` (2 minutes) is how long an admitted attempt owns its key unless renewed. Both apply when the caller does not pass an explicit value to `AdmitAsync`. Every admission and renewal duration must fall within `MinimumLeaseDuration` (1 second) and `MaximumLeaseDuration` (1 day). A hosted retention service, always registered by `AddHeadlessIdempotency`, deletes records past `retention_until` whose lease is no longer live, in batches of `PurgeBatchSize` (default 1000) every `PurgeInterval` (default 1 hour; `null` disables the service entirely). A pending record whose attempt renewed its lease past the retention is kept until that lease expires, so a live attempt never loses the record it will complete.

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

Enlisted calls (`unit.Idempotency.*`) follow the same rules as `unit.Leases` and `unit.Sequences` (see [unit-of-work.md](unit-of-work.md)):

- The relational providers need a live transaction on their own database. The in-memory provider refuses any unit over a database connection and accepts a resource-less unit (`IUnitOfWorkFactory.BeginAsync()`). The cache provider refuses every caller unit, because a cache cannot commit or roll back with one.
- Admit, complete, set-recovery-point, and release mark an observed-mode unit non-retryable. The fence read does not.

`unit.Idempotency.FenceAsync` takes the record row with an update-intent lock held until the unit ends, then refuses with `StaleAdmissionException` unless the record is still pending at the admission's generation with a live lease by the database clock. A concurrent admission of the key waits on that row and then sees the unit's committed outcome.

With `Headless.UnitOfWork.Analyzers` referenced, [HF2005](unit-of-work.md#hf2005) reports an `IIdempotentOperations` admit, complete, set-recovery-point, or release made while a unit of work is in scope, and its code fix moves the call onto `unit.Idempotency`. The cache provider refuses every `unit.Idempotency` call and SQLite refuses enlisted admission, so a host on those providers sets `dotnet_diagnostic.HF2005.severity = none` rather than applying the fix.

## HTTP composition

`Headless.Idempotency.ResponseReplay` is a thin HTTP adapter over this store. See [§ Headless.Idempotency.ResponseReplay](#headlessidempotencyresponsereplay) for its options and setup. What it adds on top of `IIdempotentOperations`:

- **Store key.** The SHA-256 hex of a derived scope string (user, method, path, query, header key), always 64 characters, so a long path or a 255-character header value stays within `IdempotencyFieldLimits.KeyMaxLength`.
- **Store tenant.** The middleware admits and peeks under the authenticated principal's tenant claim (the claim type `HttpTenantResolutionOptions.ClaimType` names, `tenant_id` by default), switching `ICurrentTenant` only for those store calls; the handler still runs under the ambient tenant. It never keys by the ambient tenant, because pre-auth catalog resolution sets that from a host or header the caller controls, which would let an anonymous caller choose a tenant's namespace to pre-seed or replay. A principal without a tenant claim, and every anonymous request, keys under the host scope. A `KeyDeriver` changes only the scope string, never the store tenant.
- **Anonymous endpoints.** With `RequireUserIdentity = false`, every anonymous request shares one namespace across all tenants: the same key on the same method, path, and query replays across tenants routed by host or header, and any caller can pre-seed a key an anonymous sender will use later. Give webhook receivers and similar endpoints a `KeyDeriver` whose scope carries a verified per-sender discriminator (for example the account a signature was verified for). Claim-less authenticated principals likewise share the host scope, so their records stay apart only while user ids are unique across tenants.
- **Admitted.** The middleware runs the handler behind a lease-renewal loop (`RenewAsync` every third of the configured lease duration, each call bounded by that same interval, since a handler's own fenced transaction can block a renewal on the record row), then completes with the captured response once `ShouldCacheResponse` accepts it, or releases otherwise.
- **The post-commit completion window.** Completion runs *after* the handler's unit commits, not inside it. A crash between the handler's commit and the middleware's completion call leaves the record `Pending`; the next request is `Admitted` as a takeover (`IsTakeover = true`) and runs the handler again. A handler reads its admission with `HttpContext.GetHttpIdempotencyContext()` (`IHttpIdempotencyContext`). To close the window, it records a ["done" recovery point](#recovery-points) carrying its response state in the same unit as its writes, with `unit.Idempotency.SetRecoveryPointAsync(context.Admission, "done", state, contract)`. On the retry, it finds `context.RecoveryPoint` and rebuilds the response from that state instead of repeating the work. It can also fence its own writes with `unit.Idempotency.FenceAsync(context.Admission)`, so a previous attempt that is still running after its own takeover cannot also commit. When the fence or a recovery point throws `StaleAdmissionException` out of the handler, the middleware releases the admission and answers `409 g:idempotency_in_flight` (no `Retry-After`), the same answer as a concurrent request, unless the response already started; then the exception propagates.
- **Replay.** Writes the stored response verbatim (status, allowlisted headers, body). The stored `Content-Encoding` is the one the response carried when the handler's first byte or flush left the middleware, which is the coding of the captured bytes. Response compression that wraps the middleware (the `UseHeadless()` order) labels the headers only after that and encodes after the capture, so its label is not stored: the replay passes through the compressor again and is encoded for whatever the retry accepts. **Conflict.** Returns the configured mismatch status.
- **Required key.** An endpoint marked `.RequireIdempotencyKey()` refuses a request without a key (`400 g:idempotency_key_required`) instead of running it unprotected.
- **Opt-out.** An endpoint marked `[DisableResponseReplay]` or `.DisableResponseReplay()` always runs its handler and never has its response stored, for an endpoint that must audit every call or whose body must never be persisted.
- **Reconciliation.** `IHttpIdempotencyLookup` reads an earlier request's status or replays its stored response from another endpoint, such as `GET /transactions/{key}` that a payment terminal polls after a timeout. It addresses the same record the middleware stored for that method, path, query, key, user, and tenant claim, through `PeekAsync` and `GetResultAsync`.
- **InFlight.** `Reject` (default): `409 g:idempotency_in_flight`. `WaitAndReplay`: each tick of a doubling backoff calls the cheap `PeekAsync` first and only re-runs the full, row-locking `AdmitAsync` when the peek shows the record no longer `Pending` (completed, released, or gone) or the holder's last-known lease expiry has passed, so a holder that releases (a thrown handler or an uncached response) frees the waiters at their next tick. It polls until `Replay`, `Admitted` (a takeover), or the configured timeout: `409 g:idempotency_in_flight_timeout`. Both 409s carry `Retry-After` until the holder's lease runs out.

## Inspecting a record

`IIdempotentOperations.InspectAsync(key, ct)` answers "was this operation admitted, is it running, or did it complete?" for one key in the current tenant. It takes no row lock, never admits or changes the record, and never returns the stored result or recovery-state bytes. Keys cannot be listed. To inspect another tenant's key, change the current tenant around the call (`currentTenant.Change(tenantId)`) inside your own authorization check; there is no cross-tenant overload.

| `IdempotencyRecordSnapshot` field | Meaning |
| --- | --- |
| `Key` | The tenant and key. |
| `Status` | `Pending` (a live lease holds it), `Completed`, or `Released` (released, lease expired, or never admitted). Never `Absent`: an absent record returns `null`. |
| `Generation` | The last admitted attempt's generation, kept after it completes or releases; `null` before any admission. |
| `TakeoverCount` | Takeovers from expired attempts since an attempt last completed or released; `null` on the cache provider. |
| `Fingerprint` | The stored request fingerprint: `Algorithm` and the digest `Hash`. |
| `LeaseExpiresAt`, `IsLeaseExpired` | The lease end while an attempt holds it, and whether it already passed `ObservedAt` (the attempt stalled). `null` and `false` once completed or released. |
| `RetentionUntil` | When the record stops replaying. |
| `ObservedAt` | The store's clock when it read the record. Compare the other instants with it, never with your own clock. |
| `ResultContract`, `ResultSizeBytes` | The stored result's contract and size; `null` unless completed. |
| `RecoveryPoint`, `RecoveryContract`, `RecoveryStateSizeBytes` | The last recovery point's name, contract, and state size; `null` when none is kept. |

No provider stores when a record was admitted or completed, or the HTTP status of a stored response, so the snapshot has no such fields.

| Provider | Clock | Notes |
| --- | --- | --- |
| PostgreSQL, SQLite | Database clock, same statement | Sizes are measured in SQL; the body never leaves the database. |
| SQL Server | `SYSUTCDATETIME()`, same statement | With `READ_COMMITTED_SNAPSHOT` off, the read waits behind an uncommitted writer, as `PeekAsync` does. |
| InMemory | Registered `TimeProvider` | Reads the committed record. |
| Caching | Registered `TimeProvider` | Reads the whole cache entry, body included, and drops the body before returning. `TakeoverCount` is `null`. |

`Describe()` returns `IdempotencyStoreDescription(Provider, Location, DefaultRetention, DefaultLeaseDuration, MinimumLeaseDuration, MaximumLeaseDuration, PurgeInterval, PurgeBatchSize)`. `Location` is the quoted, qualified table name the provider's SQL uses on a relational provider (SQLite folds the schema into the name, `"headless_idempotency_records"`), the key prefix on the cache provider, and `null` in memory. `PurgeInterval` is `null` when the purge is off, and on the cache provider, whose entries expire on their own.

**Verify:** admit a key, then `InspectAsync` returns `Status` `Pending` with `Generation` equal to the admission's; after `CompleteAsync`, it returns `Completed` with `ResultSizeBytes` equal to the stored result's length, and `GetResultAsync` is never called.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Idempotency.PostgreSql` | Records must survive restarts and be shared by every instance, and enlisted units run on PostgreSQL | Units run on SQL Server | The database clock (`clock_timestamp()`) decides |
| `Headless.Idempotency.SqlServer` | The same, with units on SQL Server | Units run on PostgreSQL | The database clock (`SYSUTCDATETIME()`) decides; peeks can wait on writers without read committed snapshot isolation |
| `Headless.Idempotency.Sqlite` | Embedded or single-host applications that keep their data in a SQLite file | The operation must be admitted inside a caller's unit (`unit.Idempotency.AdmitAsync`), or many writers contend | Every call holds the database write lock; enlisted admission is refused, autonomous admission and every other enlisted call work; the host clock decides |
| `Headless.Idempotency.InMemory` | Tests, local development, or a single-instance host with no relational database | Several instances serve the same keys, or retries must be deduplicated across a restart | Records live in one process and vanish on restart; the registered `TimeProvider` decides leases and retention; enlisted calls run only on resource-less units, so a fence cannot join a database transaction |
| `Headless.Idempotency.Caching` | Several replicas share a Redis cache and there is no SQL database, and every call is autonomous (`IIdempotentOperations`, the HTTP middleware) | A result must survive a cache flush or eviction, or the operation needs `unit.Idempotency`, `FenceAsync`, or a recovery point recorded inside a unit | Autonomous only; durability is the cache's, so an evicted or flushed record means the operation runs again; the application clock decides leases, so replica clock skew shifts expiry; completion is still compare-and-swap guarded by generation, so a zombie never overwrites a newer result |

---

## Headless.Idempotency.Abstractions

Consumer contracts: `IIdempotentOperations`, `IdempotentAdmission`, `IdempotencyFingerprint`, `IdempotencyKey`, `IdempotentResult`, the JSON helpers, and the `unit.Idempotency` accessor.

### Setup

```bash
dotnet add package Headless.Idempotency.Abstractions
```

Reference it from code that admits, completes, or fences idempotent operations. Registration lives in `Headless.Idempotency` and the provider packages.

**Verify:** nothing to verify alone. Reading `unit.Idempotency` throws `InvalidOperationException` naming `AddHeadlessIdempotency` when no provider is registered.

### Design and runtime behavior

- `IIdempotentOperations.AdmitAsync(key, fingerprint, expectedContract?, leaseDuration?, retention?, ct)` → `IdempotentAdmission`. `CompleteAsync(admission, result, contract, retention?, ct)` stores the result and ends the lease in one transaction; throws `StaleAdmissionException` when the attempt no longer owns the key. `ReleaseAsync(admission, ct)` → `IdempotentLeaseStatus` (`Released` on success or when the key is already released; `Expired`, `Stale`, or `Completed` for a refusal that wrote nothing). `SetRecoveryPointAsync(admission, point, state, contract, ct)` records the attempt's last finished step in its own committed transaction, fenced like a completion. `RenewAsync(admission, duration, ct)` → `IdempotentLeaseRenewal(Status, ExpiresAt)`, with `IsRenewed` when `Status` is `Current` (autonomous only). `PeekAsync(key, ct)` → `IdempotencyPeekStatus`: `Absent` (no record, or past its retention), `Pending` (an attempt holds it under a live lease), `Released` (not completed and no live lease: the last attempt released it or its lease expired, so the next admission takes it), or `Completed`. It takes no row lock and returns no full disposition. It exists to poll cheaply while waiting on another attempt, not to replace `AdmitAsync`. `GetResultAsync(key, ct)` → `IdempotentResult?` reads the stored result of a completed record within its retention, with the same lock-free read; it returns `null` for an absent, pending, or expired record and never admits an attempt, so a client that lost a response can ask what happened without starting the operation again.
- `InspectAsync(key, ct)` → `IdempotencyRecordSnapshot?` reads the record's metadata for the current tenant with the same lock-free read, and returns `null` for an absent record or one past its retention. See [Inspecting a record](#inspecting-a-record). `Describe()` → `IdempotencyStoreDescription` reports the provider package, where its records live, and the configured defaults, without I/O.
- `IdempotentAdmission`: `Disposition`, `Key` (`IdempotencyKey(TenantId, Key)`), `Fingerprint`, `Generation`/`LeaseExpiresAt` (when relevant), `IsTakeover`, `Retention`, `RecoveryPoint` (`IdempotentRecoveryPoint`: `Name`, `State`, `Contract`), `Result`, `StoredFingerprint`, `StoredContract`, and `IsAdmitted` (`[MemberNotNullWhen(true, nameof(Generation), nameof(LeaseExpiresAt), nameof(Retention))]`).
- `IdempotentLeaseStatus` is what the store found for an attempt's generation: `Current`, `Expired`, `Stale`, `Completed`, or `Released`. `StaleAdmissionException` (an `InvalidOperationException`) carries the refused `Key`, `Generation`, and `Reason`.
- `unit.Idempotency` (namespace `Headless.UnitOfWork`) returns a `UnitOfWorkIdempotency` bound to the unit; repeated reads on one unit return the same instance. It mirrors `AdmitAsync`/`CompleteAsync`/`SetRecoveryPointAsync`/`ReleaseAsync` plus `FenceAsync(admission, ct)`, which throws `StaleAdmissionException` rather than returning a status. There is no enlisted `RenewAsync`.
- `unit.Idempotency` throws `InvalidOperationException` naming `AddHeadlessIdempotency` when no provider registered the feature.

---

## Headless.Idempotency

Registration, fingerprint and key resolution, admission orchestration, and the retention purge.

### Setup

```bash
dotnet add package Headless.Idempotency
```

Applications reach it through a provider package, as shown in [Orientation](#orientation).

**Verify:** `AddHeadlessIdempotency` throws `InvalidOperationException` when no provider or more than one is chosen. An admission or renewal duration outside the configured bounds throws `ArgumentOutOfRangeException`.

### Configuration

| Builder member | Effect |
| --- | --- |
| `ConfigureOptions(Action<IdempotentOperationsOptions>)` | Sets `DefaultRetention` (24 hours), `DefaultLeaseDuration` (2 minutes), `MinimumLeaseDuration` (1 second), `MaximumLeaseDuration` (1 day), `PurgeInterval` (1 hour; `null` disables the purge), `PurgeBatchSize` (1000). Every lease duration is checked against the bounds and then truncated to whole microseconds, as is every retention |
| `ConfigureStorage(Action<IdempotencyStorageOptions>)` / `ConfigureStorage(IConfiguration)` | Sets `Schema` (default `"headless"`, the schema every Headless feature shares), the schema the record table and its generation sequence live in (`idempotency_records` and `idempotency_record_generations` on PostgreSQL, `IdempotencyRecords` and `IdempotencyRecordGenerations` on SQL Server) |

### Design and runtime behavior

- `AddHeadlessIdempotency` requires exactly one `Use…` provider call and nothing else. When the host registered no `ICurrentTenant`, it registers the async-local one, because records are keyed by the current tenant.
- It registers `IIdempotentOperations` and `IUnitOfWorkIdempotency` as singletons and always adds the retention hosted service. With `PurgeInterval` `null` the service stays idle and re-checks the option every minute, so a configuration reload back to a value resumes purging without a restart.
- `IIdempotencyRecordStore` is the provider seam. Applications do not call it; see [For store authors](#for-store-authors).
- On the relational providers, every decision reads the database clock after the record's row lock is held. A repeated completion or release by the same attempt writes nothing. A lease that runs out between the locked read and the write is refused: completion and the recovery point throw `StaleAdmissionException` with `Reason` `Expired`, and a release answers `Expired`. Each statement is bounded by the provider's `CommandTimeout`. Retention is set to the later of the stored value and the database clock plus the retention.
- On the relational providers, peek and result reads are one statement on their own connection with no transaction, and retry any transient fault up to 3 attempts. Renewal and purge run in their own READ COMMITTED transaction and retry a transient fault raised before the commit (a deadlock, a serialization conflict, a lock timeout, a dropped connection) up to 3 attempts, waiting a jittered delay (`n × 10–50 ms` before retry `n`, on the registered `TimeProvider`). A fault from the commit is never retried. Enlisted calls never retry.
- `PostgreSqlIdempotencyOptions` and `SqlServerIdempotencyOptions` both derive from `RelationalIdempotencyOptions`, which carries `ConnectionString` and `CommandTimeout`.
- On the cache provider, a write another caller beat raises `IdempotencyRecordConflictException` internally. The autonomous `AdmitAsync`, `CompleteAsync`, `SetRecoveryPointAsync`, and `ReleaseAsync` then run again, whole, at most 8 attempts with no delay between them. The locking providers never throw it.

### For store authors

To store records elsewhere, implement `IIdempotencyRecordStore` and register it from your own `IIdempotencyProviderOptionsExtension`: its `AddServices(services)` registers the store as a singleton (`services.TryAddSingleton<IIdempotencyRecordStore, TStore>()`) and the unit-of-work factory (`services.AddUnitOfWork()`). Expose a `Use…` extension that calls `setup.RegisterExtension(...)` on `HeadlessIdempotencySetupBuilder`, so `AddHeadlessIdempotency` counts it as the one provider. Argument, tenant, duration, and unit-state checks run before a call reaches the store.

A record is one row per `IdempotencyRecordKey(TenantId, Key)`. It holds the fingerprint, the attempt's generation and lease expiry, the status (`IdempotencyRecordStatus.Pending` or `Completed`), the result bytes and contract, the last recovery point (name, state, and contract, all present or all absent), and its retention instant. The locking reads return it as `IdempotencyRecordState`.

Rules every store honors:

- **One clock.** Every time comparison uses the store's clock, read after the record's lock is held, never the application clock.
- **Generations only grow per key**, even after the record is purged and the key admitted again. Draw them from one store-wide counter, and only after the record is locked.
- **One lock per record.** Every verb that reads or writes a record takes, or already holds, an update-intent lock that lasts until the transaction ends. Never take a shared lock: an admission queued behind it would deadlock against the fencing unit's own completion.
- **Enlisted verbs** run inside the given unit, never commit, and never retry.
- **Retention only grows.** Every write that sets retention sets it to the later of its current value and the store clock plus the given retention.

| Member | What it must do |
| --- | --- |
| `BeginOwnedUnitAsync(ct)` | Begin a unit that `ValidateEnlistment` accepts (for a relational store, on a new connection to its configured database), so an autonomous call can change a record in a unit it commits. |
| `ValidateEnlistment(unit)` | Throw `InvalidOperationException` when the unit cannot host the store's commands: no live transaction of the store's own provider, another database, or a closed connection. An in-process store refuses a unit whose work commits in a database. |
| `LockOrInsertAsync(unit, key, fingerprint, retention, ct)` | Lock the record for update, inserting it first when absent. An inserted row is `Pending` with the fingerprint, no generation, lease, or result, and retention at the store clock plus `retention`. Never raise a unique-key violation: a losing concurrent insert waits for and then locks the winner's row. |
| `LockAsync(unit, key, ct)` | Lock an existing record for update and return it, or `null` when none exists. Never insert. |
| `AdmitAsync(unit, key, fingerprint, leaseDuration, retention, keepRecoveryPoint, ct)` | On the locked record, draw the next generation, set the lease expiry to the store clock plus `leaseDuration`, and write `Pending`, the fingerprint, no result, and extended retention. Keep the recovery point only when `keepRecoveryPoint` is set. Return the `IdempotencyRecordGrant(Generation, LeaseExpiresAt)`. A record past retention is reset in place this way. |
| `CompleteAsync(unit, key, generation, result, contract, retention, ct)` | On the record locked and held by `generation`, write `Completed`, the result and contract, no lease expiry (keep the generation), no recovery point, and extended retention. A record this generation already completed is left as it is. |
| `ReleaseAsync(unit, key, generation, retention, ct)` | On the record locked and held by `generation`, write `Pending` with no generation or lease, keep the recovery point, and extend retention. A record this generation already released is left as it is. |
| `SetRecoveryPointAsync(unit, key, generation, point, state, contract, ct)` | Replace the recovery point on the record locked and held by `generation`. Change nothing else. |
| `RenewAsync(key, generation, leaseDuration, ct)` | On the store's own connection, committed before it returns: extend the lease to the store clock plus `leaseDuration` only while the record is pending at that generation with a live lease. Otherwise write nothing and return `IdempotentLeaseRenewal` with the reason. Retry a deadlock in a fresh transaction. |
| `PeekAsync(key, ct)` | Read the status on the store's own connection without the record lock. Return `Absent` when no record exists or its retention has passed, `Completed` for a completed record, `Pending` when a generation holds a live lease, and otherwise `Released`. |
| `GetResultAsync(key, ct)` | Read the stored result of a completed record within retention, with the same lock-free read; otherwise `null`. |
| `InspectAsync(key, ct)` | Read the record's metadata with the same lock-free read, judged on one store clock reading returned as `ObservedAt`: `null` when absent or past retention. Report the generation of the last admitted attempt (`null` when never admitted), the lease expiry only while an attempt holds the lease, and the result and recovery-state sizes, never their bytes. A field the store does not keep is `null`. |
| `Info` | Return `IdempotencyStoreInfo(Provider, Location, PurgeDeletesRecords)` without I/O. Answer `PurgeDeletesRecords: false` when `PurgeAsync` never deletes anything. |
| `PurgeAsync(olderThan, limit, ct)` | On the store's own connection, committed before it returns, delete at most `limit` records whose retention ended at least `olderThan` ago and whose lease is no longer live. Skip a record another transaction holds instead of waiting. Return the count deleted. |

A store that checks at write time instead of locking at read time (an optimistic store, such as the cache provider) throws `IdempotencyRecordConflictException(key)` from a write another caller beat. The autonomous `AdmitAsync`, `CompleteAsync`, `SetRecoveryPointAsync`, and `ReleaseAsync` then run again from a fresh read, up to 8 attempts.

A relational store can build its statements on the SQL store kit; see [Store statement kit](sql.md#store-statement-kit-for-provider-authors).

**Verify:** with your `Use…` call, `AddHeadlessIdempotency` resolves `IIdempotentOperations`. In a test, two concurrent `AdmitAsync` calls on one key give one `Admitted` and one `InFlight`, a second `CompleteAsync` by the same attempt throws `StaleAdmissionException` with `Reason` `Completed`, and a key admitted again after `PurgeAsync` gets a higher `Generation`.

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

`UseInMemory()` takes no options. It registers `TimeProvider.System` and the unit-of-work factory when the host has not. Register a `FakeTimeProvider` first to drive leases and retention in tests. `ConfigureStorage` has no effect on it.

**Verify:** no startup signal. In a test, admit a key, advance the `FakeTimeProvider` past the lease, and assert the next admission is `Admitted` with `IsTakeover` `true`.

### Design and runtime behavior

- Records live in a singleton table in this process and disappear when it stops. They deduplicate only the requests this process handles: behind a load balancer each instance keeps its own records, so the same key can run once per instance. Use a relational provider when several instances serve the same keys.
- The registered `TimeProvider` decides lease expiry and retention, read after the record's lock is held. A lease or retention that ends at the clock's instant has already ended.
- Every admission, fence, completion, recovery point, and release takes an exclusive per-key lock, so admission decides exactly as on the relational providers. Generations come from one process-wide counter, so they grow per key across releases, takeovers, and purges, but restart from 1 when the process restarts, when every earlier record is gone too.
- Autonomous calls run in a resource-less owned unit the store begins. Enlisted calls (`unit.Idempotency`) run only on a resource-less unit (`IUnitOfWorkFactory.BeginAsync()`); a unit over a database connection or `DbContext` is refused with `InvalidOperationException`, because in-memory records cannot commit or roll back with that transaction. The unit is the commit boundary: the record's lock is held until the unit ends, its writes reach the table only when the unit completes, and a rollback or a dispose without completing drops them. One unit may fence and then complete the same record without waiting on itself.
- `FenceAsync` holds the record until the unit ends, so no admission in this process can take the key over under the unit. It guards nothing outside the process and is not coupled to any database transaction.
- `PeekAsync`, `GetResultAsync`, and `InspectAsync` read the committed record without the lock and never see a unit's uncommitted writes. `RenewAsync` takes the lock and commits at once.
- There is no deadlock detection. Units that lock several keys must lock them in one consistent order. Pass a cancellation token that bounds the wait.
- The retention purge skips a record a unit holds and keeps a record whose lease is still live, like the relational providers.
- `Headless.Idempotency.ResponseReplay` works on it unchanged: `AddHeadlessIdempotency(setup => setup.UseInMemory())` plus `AddHeadlessHttpIdempotency(...)` needs no database.

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

`UseCache()` needs a caching provider registered through `AddHeadlessCaching`. It also has `UseCache(Action<CacheIdempotencyOptions>)`, `UseCache(Action<CacheIdempotencyOptions, IServiceProvider>)`, and `UseCache(IConfiguration)`. It registers `TimeProvider.System` and the unit-of-work factory when the host has not. `ConfigureStorage` has no effect on it.

**Verify:** host startup fails without a caching registration. Resolving `IIdempotentOperations` throws `InvalidOperationException` when the selected cache is a hybrid. No probe reaches Redis at startup; the first call fails when it is unreachable.

### Configuration

| `CacheIdempotencyOptions` | Default | Effect |
| --- | --- | --- |
| `KeyPrefix` | `headless:idempotency:` | Prefix of every entry: `{prefix}record:{tenant length}:{tenant}{key}` per record and `{prefix}generation` for the counter. Give applications that share a cache distinct prefixes. Environments that share one cache need no change: the cache puts the host-wide `Headless:Prefix` in front of every key ([Isolation prefix](utilities.md#isolation-prefix)). |
| `CacheName` | `null` | A keyed `ICache` instance to hold the records. `null` uses the registered `IRemoteCache` when there is one, otherwise the default `ICache`. The cache must be single-tier (an `IRemoteCache` or `IInMemoryCache`): resolving the store fails with `InvalidOperationException` on a hybrid, because its reads come from a local copy that lags other replicas' writes. With a default hybrid whose L2 is a named Redis instance (`HybridCacheOptions.RemoteCacheName`), set `CacheName` to that instance. |

### Design and runtime behavior

- **Autonomous only.** `IIdempotentOperations` and the HTTP middleware work. Every `unit.Idempotency` call is refused with `InvalidOperationException`: that includes enlisted admission, completion, and release, `FenceAsync`, and `SetRecoveryPointAsync` inside a unit. A cache cannot commit or roll back with a unit of work, so use a relational provider (or `UseInMemory` in one process) for those. The autonomous `SetRecoveryPointAsync` works.
- **One entry per record.** Each key's record is one cache entry holding the whole record, result included, as bytes. Every change is a compare-and-swap against the exact entry the call read. A call that loses the race re-reads and decides again (see [Headless.Idempotency](#headlessidempotency)), so concurrent admissions converge like locked ones: one `Admitted`, the others `InFlight`, `Replay`, or `Conflict`. A write the cache declines while the entry is unchanged (for example an entry over an in-memory cache's size limit) throws `InvalidOperationException` instead of retrying.
- **Completion is fenced by generation.** Complete, release, recovery point, and renewal each write only while the entry is pending at the attempt's generation with a live lease, and the swap fails if anything changed since that check. An attempt that lost the key gets `StaleAdmissionException` and writes nothing, so a zombie never overwrites a newer result. There is no lock between the check and the swap, so a slow attempt's write races a takeover through the swap rather than waiting on a row lock.
- **Application clock.** Lease expiry and retention are instants from the registered `TimeProvider`, stored in the entry. Replicas whose clocks disagree disagree on when a lease expires: a replica running ahead takes over a live attempt early. Keep lease durations well above the expected skew.
- **Retention is the cache's expiry.** Each entry expires in the cache at the later of its retention and its lease, so a live attempt keeps its record even past retention. `PurgeAsync` deletes nothing and the retention service has nothing to do; set `PurgeInterval = null` to keep it idle.
- **Durability is the cache's.** An evicted or flushed record is a record that never existed: the next admission of the key is a fresh `Admitted`, not a `Replay`, and the operation runs again. When that matters, run Redis with a no-eviction policy (or `volatile-*` with enough memory) and persistence.
- **Generations.** Drawn from a cache counter (`IncrementAsync`) that never expires. A generation is never issued at or below the highest one the record ever admitted, and the counter is raised past it, so generations keep growing per key even if the counter is evicted. Losing the counter and the record together can restart generations from 1.
- `PeekAsync`, `GetResultAsync`, and `InspectAsync` are one cache read each. `InspectAsync` transfers the whole entry, result included, and returns only its size; it reports no takeover count, and `Describe()` reports no purge schedule. `RenewAsync` is its own compare-and-swap loop with the same bound as the autonomous calls.
- `Headless.Idempotency.ResponseReplay` works on it unchanged, including `WaitAndReplay`, since the middleware uses only the autonomous surface. A handler that calls `unit.Idempotency.FenceAsync` or records a recovery point inside its unit is refused.

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

**Verify:** `dotnet MyApp.dll resources provision -k Schema` reports `Ok` for the `PostgreSql/headless/Idempotency` resource. A host started before that fails, naming the resource and `resources provision`.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database that holds the records; an enlisted call is accepted only on a unit whose connection reaches it |
| `CommandTimeout` | 30 seconds | Also bounds how long a concurrent admission of the same key waits behind an open enlisted admission, fence, or completion |

### Design and runtime behavior

- The verbs are the shared relational store's (see [Headless.Idempotency](#headlessidempotency)). Two first admissions of one key never raise a unique violation, so the caller's PostgreSQL transaction is never aborted by one.
- Every decision reads `clock_timestamp()` after the row lock, so an expired lease never looks live inside a long enlisted unit.
- The autonomous path runs in its own READ COMMITTED transaction. The enlisted path runs on the unit's own connection and transaction with no retry.
- Autonomous renewal, peek, and purge retry a transient fault raised before the commit, including a deadlock or serialization failure (`40P01`, `40001`) and anything Npgsql reports as transient. The purge skips rows another transaction holds.
- The sequence, table, and index are one step `resources provision` applies through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts), recorded as `Idempotency/1`. A host start only verifies the step and fails fast when it is missing; see [Resource provisioning](utilities.md#resource-provisioning).

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

**Verify:** `dotnet MyApp.dll resources provision -k Schema` reports `Ok` for the `SqlServer/headless/Idempotency` resource. A host started before that fails, naming the resource and `resources provision`.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database that holds the records; an enlisted call is accepted only on a unit whose connection reaches it. Name the database explicitly (`Initial Catalog`) |
| `CommandTimeout` | 30 seconds | Also bounds how long a concurrent admission of the same key waits behind an open enlisted admission, fence, or completion |

### Design and runtime behavior

- The verbs are the shared relational store's (see [Headless.Idempotency](#headlessidempotency)). Two first admissions of one key serialize on a key-range lock and never raise a duplicate key, so a caller's `XACT_ABORT ON` transaction is never doomed by one.
- Peek and inspect read the record with no lock hint, so whether they wait depends on the database. With `READ_COMMITTED_SNAPSHOT` on, it reads the last committed row version and never waits. With it off, READ COMMITTED takes a shared lock on the row, so a peek waits behind any admission, fence, completion, or release that has written the row and not yet committed, up to `CommandTimeout`. An enlisted unit holds that lock until it ends, so turn on read committed snapshot isolation when `WaitAndReplay` polls keys that enlisted units hold.
- Each decision reads `SYSUTCDATETIME()` after the lock wait, so a wait never makes a lease look live past its expiry.
- The autonomous path runs in its own READ COMMITTED transaction. The enlisted path runs on the unit's own connection and transaction with no retry.
- Autonomous renewal, peek, and purge retry a transient fault raised before the commit, including a deadlock or snapshot update conflict (1205, 3960), a lock timeout (1222), and the connection faults EF Core's SQL Server retry set covers. The purge skips rows another transaction holds, with or without read committed snapshot isolation.
- The sequence, table, and index are one step `resources provision` applies through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts), recorded as `Idempotency/1`.

---

## Headless.Idempotency.Sqlite

SQLite storage for idempotency records.

### Setup

```bash
dotnet add package Headless.Idempotency.Sqlite
```

```csharp
builder.Services.AddHeadlessIdempotency(setup => setup.UseSqlite("Data Source=/var/lib/app/app.db"));
// or reuse the connection from services.AddSqliteSql(connectionString): setup.UseSqlite();
```

Use a database file: the store and the schema runner open their own connections, so a private `:memory:` database would vanish between them.

**Verify:** `dotnet MyApp.dll resources provision -k Schema` reports `Ok` for the `Sqlite/headless/Idempotency` resource. A host started before that fails, naming the resource and `resources provision`.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database file that holds the records; an enlisted call is accepted only on a SQLite unit whose connection reaches the same file |
| `CommandTimeout` | 30 seconds | Also bounds how long a call waits for another writer's database lock |

### Design and runtime behavior

- **Enlisted admission is refused.** `unit.Idempotency.AdmitAsync` throws `NotSupportedException` before it touches the unit. SQLite has no counter that ignores transactions, so a generation drawn in a rolled-back unit would be drawn again, and the rolled-back admission could complete the new attempt's record. Use `IIdempotentOperations.AdmitAsync` (or the HTTP middleware), which commits before it returns the generation. The unit can still complete, release, fence, or set a recovery point of an autonomously admitted attempt.
- The verbs are the shared relational store's (see [Headless.Idempotency](#headlessidempotency)). Every transaction begins `IMMEDIATE` and holds the database write lock, which stands in for the row lock: admissions, fences, and completions of every key serialize on the file. A peek runs outside any transaction and takes no write lock, so it reads the last committed record without waiting on a writer.
- A purge never resets generations: a key admitted again after its record was purged still gets a higher generation.
- `SQLITE_BUSY` before the commit (another writer held the database past the connection's `Default Timeout`) is retried in a fresh transaction, like a lock timeout on the other providers. The wait holds a thread-pool thread; see [sql.md § SQLite in the kit](sql.md#sqlite-in-the-kit).
- The record table and its generation counter are one step `resources provision` applies through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts), recorded as `Idempotency/1`.

---

## Headless.Idempotency.ResponseReplay

Stripe-style HTTP idempotency middleware for ASP.NET Core. Admits each request through the durable `Headless.Idempotency` store, captures the full HTTP response (status, allowlisted headers, byte body) on first execution, and replays it byte-equivalent on identical retries. See [Core Concepts](#core-concepts) and [HTTP composition](#http-composition) for the store's admission contract, retention, and takeover semantics. This section documents only the HTTP adapter.

### API and behavior

- Byte-equivalent replay of captured responses.
- Two in-flight strategies: `InFlightStrategy.Reject` (default: 409 `g:idempotency_in_flight`) and `InFlightStrategy.WaitAndReplay` (polls the durable store with a doubling backoff, capped at 1 second per poll, until `InFlightLockTimeout` elapses: 409 `g:idempotency_in_flight_timeout`). Both 409s carry `Retry-After`: the seconds, rounded up, until the holding attempt's lease runs out, which is when a retry can be admitted as a takeover.
- Required keys per endpoint: `.RequireIdempotencyKey()` (or `KeyRequired = true`) refuses a request the middleware would handle that carries no key with 400 `g:idempotency_key_required`, so a money-moving endpoint never runs without duplicate protection. Off by default.
- Per-endpoint opt-out: `DisableResponseReplayAttribute` on a controller or action, or `.DisableResponseReplay()` on a Minimal API endpoint or route group (and `MapControllers()`), passes the endpoint's requests straight through, as a `ShouldApply` that returns `false` does. A key the request carries is ignored: not validated, admitted, stored, or replayed, so every retry runs the handler again and the handler sees no `IHttpIdempotencyContext`. A request without a key is not refused, even under `KeyRequired`. The opt-out wins over `.WithIdempotency(...)` and `.RequireIdempotencyKey()` on the endpoint, its controller, its route group, or `MapControllers()`, in any order; nothing re-enables replay below it.
- Reconciliation reads through `IHttpIdempotencyLookup` (scoped): `GetStatusAsync(context, target)` → `IdempotencyPeekStatus`, and `TryReplayAsync(context, target)` writes an earlier request's stored response to the current one. `target` is an `IdempotentRequestTarget(Method, Path, Key)` with an optional `Query`.
- Independent request-body memory threshold and fingerprinting cap, with `OversizeBehavior.Reject` (413) or `OversizeBehavior.PassThrough` behaviors.
- Header allowlist filters `Set-Cookie`, `traceparent`, and other sensitive or per-request headers from the captured response.
- Store key: the lowercase SHA-256 hex of a scope string (`idem:{userId}:{METHOD}:{path}{?query}:{key}` by default, query string included so endpoints branching on query params don't cross-replay); the durable store additionally scopes every key by the current tenant, so the scope itself carries no tenant segment.
- Per-endpoint overrides via `.WithIdempotency(o => ...)` and `.RequireIdempotencyKey()`; every attachment applies, in the order the endpoint declares it. `HeaderName` is excluded (see Design constraints).
- OpenAPI description: `.RequireIdempotencyKey()` and `.WithIdempotency(...)` also attach `HttpContractMetadata` (`Headless.Api.Abstractions`), which `Headless.OpenApi` renders: the key header (named by the application-level `HeaderName`, required when `KeyRequired` is on), 400 for a missing or malformed key, 409 for a key another attempt holds (with `Retry-After`), the reused-key `MismatchStatusCode` (409 or 422), 413 when `OversizeBehavior` is `Reject`, and `Idempotent-Replayed` on 2xx responses. It reflects the application-level options and every override of the endpoint, its route group's included, read once when the conventions run; an endpoint whose methods are all outside `Methods`, or that opted out with `DisableResponseReplay`, gets none. It describes a controller action too when the convention is on `MapControllers()` (`MapControllers().RequireIdempotencyKey()`). Endpoints the middleware handles with no override are not described.
- Custom hooks: `KeyDeriver`, `RequestFingerprint`, `ShouldApply`, `ShouldCacheResponse`.
- Default cache predicate: 2xx + selected 4xx; never 5xx, 1xx, 3xx, or transient 4xx (408/425/429).
- A handler reached through the middleware reads its admission with `HttpContext.GetHttpIdempotencyContext()` (`IHttpIdempotencyContext`: `HeaderKey`, `Scope`, `Key`, `Admission`, `Generation`, `IsTakeover`, `RecoveryPoint`), fences its own writes with `unit.Idempotency.FenceAsync(context.Admission)`, and records progress with `unit.Idempotency.SetRecoveryPointAsync(context.Admission, ...)`.
- `HttpIdempotencyErrorCodes` static class: `KeyReused`, `InFlight`, `InFlightTimeout`, `BodyTooLarge`, `KeyMalformed`, `KeyRequired` as `public const string`.

### Design constraints

The middleware is a thin adapter over `IIdempotentOperations`: it admits, runs the handler behind a lease-renewal loop (every third of `InFlightLease`), and completes with the captured response after the handler's unit commits, or releases the admission when `ShouldCacheResponse` rejects the response or the handler threw. Completion happens after commit, not inside it, so a crash in that window leaves the record `Pending`, and the next request takes the key over with `IsTakeover = true`. Recovery points close that window. The handler records a final "done" point with its response state in the same unit as its writes: `unit.Idempotency.SetRecoveryPointAsync(context.Admission, "done", state, contract)`. The takeover then finds it on `context.RecoveryPoint` and rebuilds the response from that state instead of running the work again. A multi-step handler records one point per step and resumes after the last. A release on a handler exception keeps the point, and completion clears it. See [Recovery points](#recovery-points). A handler whose side effects are not safe to repeat and that records no points should check `IsTakeover`, fence its own writes with `unit.Idempotency.FenceAsync(context.Admission)`, or both.

`.WithIdempotency()` ignores a `HeaderName` override, because the middleware reads the request header before it resolves endpoint metadata. Change `HeaderName` globally with `AddHeadlessHttpIdempotency(o => o.HeaderName = ...)`.

`RequestBodyBufferThreshold` controls when request buffering spills from memory to a temporary file. `MaxBodySizeForHashing` independently controls which bodies are eligible for idempotency. Lowering the threshold cuts memory but adds temporary-file I/O and latency; measure under your own concurrency before you change it.

### Install

```bash
dotnet add package Headless.Idempotency.ResponseReplay
```

### Setup and use

> Register a store first. This package references only `Headless.Idempotency.Abstractions`, so `IIdempotentOperations` comes from `AddHeadlessIdempotency(...)` with a provider: `UsePostgreSql` or `UseSqlServer`; `UseCache` for several replicas that share a Redis cache and have no SQL database; or `UseInMemory` for tests, local development, and single-instance hosts. Registration order does not matter.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless(); // current user, problem details, and clock services the middleware resolves
builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims()));
builder.Services.AddAuthentication(); // add your scheme, such as JWT bearer
builder.Services.AddAuthorization();
builder.Services.AddHeadlessIdempotency(setup => setup.UsePostgreSql(connectionString)); // or setup.UseSqlServer(...)
// tests, local development, one instance: AddHeadlessIdempotency(setup => setup.UseInMemory())
// replicas sharing Redis, no SQL: AddHeadlessCaching(c => c.UseRedis(...)) + AddHeadlessIdempotency(setup => setup.UseCache())
builder.Services.AddHeadlessHttpIdempotency(o =>
{
    o.Retention = TimeSpan.FromHours(24);
    o.InFlightStrategy = InFlightStrategy.Reject;
});

var app = builder.Build();

app.UseHeadless(); // includes UseResponseCompression, which must stay OUTSIDE (before) UseHeadlessHttpIdempotency
app.UseAuthentication();
app.UseHeadlessTenancy(); // tenant must be resolved before idempotency
app.UseAuthorization();
app.UseHeadlessHttpIdempotency(); // installs response-capture stream; place AFTER auth and tenancy

app.MapHeadlessEndpoints();
app.MapPost("/disbursements", CreateDisbursement);
app.Run();
```

**Verify:** a host without `AddHeadlessIdempotency(...)` fails at startup with `MissingRequiredServiceException`. In an integration test, send the same `POST` twice with one `Idempotency-Key`: the second response has the first one's status and body plus `Idempotent-Replayed: true`, and the handler ran once. The same key with a different body answers `MismatchStatusCode` (422 by default).

Per-endpoint overrides:

```csharp
app.MapPost("/webhooks", HandleWebhook)
    .WithIdempotency(o =>
    {
        o.Retention = TimeSpan.FromDays(7);
        o.MismatchStatusCode = StatusCodes.Status409Conflict;
    });
```

Requiring a key on an endpoint that moves money, and a reconciliation endpoint a client polls after a timeout:

```csharp
app.MapPost("/transactions", PostTransaction).RequireIdempotencyKey();

app.MapGet("/transactions/{key}", async (string key, HttpContext http, IHttpIdempotencyLookup lookup, CancellationToken ct) =>
{
    // Replays the stored response of POST /transactions with this key for the same user and tenant claim.
    return await lookup.TryReplayAsync(http, new IdempotentRequestTarget("POST", "/transactions", key), ct)
        ? Results.Empty
        : Results.NotFound();
});

app.MapGet("/transactions/{key}/status", async (string key, HttpContext http, IHttpIdempotencyLookup lookup, CancellationToken ct) =>
    Results.Ok(new { status = await lookup.GetStatusAsync(http, new IdempotentRequestTarget("POST", "/transactions", key), ct) })
);
```

Keeping endpoints out of replay, such as one that reveals personal data and must neither store its response nor skip its audit:

```csharp
app.MapPost("/customers/{id}/reveal", RevealCustomer).DisableResponseReplay();

var reports = app.MapGroup("/reports").DisableResponseReplay(); // every endpoint of the group

[HttpPost("{id}/reveal"), DisableResponseReplay] // MVC: on an action or a controller
public IActionResult Reveal(Guid id) => ...;
```

The lookup addresses the record the middleware stored: the earlier request's method, path, query, and key, plus the current user and tenant claim, so a lookup by another caller finds nothing. It reads the application-level options; a `KeyDeriver` runs against the lookup request's `HttpContext`, so an application whose deriver depends on the request path reads through `IIdempotentOperations.GetResultAsync` with its own store key instead. Reading never admits an attempt.

A handler fencing its own writes against the admission:

```csharp
app.MapPost("/disbursements", async (HttpContext http, IUnitOfWorkFactory factory, CancellationToken ct) =>
{
    var idem = http.GetHttpIdempotencyContext()!; // set by the middleware once the request is admitted

    return await factory.RunAsync(
        db,
        async (unit, innerCt) =>
        {
            await unit.Idempotency.FenceAsync(idem.Admission, innerCt); // throws StaleAdmissionException past takeover
            // ... business writes ...
            return Results.Ok();
        },
        cancellationToken: ct
    );
});
```

### Configuration

| Property | Default | Purpose |
|----------|---------|---------|
| `Retention` | 24 hours | How long a completed response replays, and how long a released key's record is kept, in the durable store. |
| `HeaderName` | `Idempotency-Key` | Request header carrying the key (per IETF `draft-ietf-httpapi-idempotency-key-header`). |
| `KeyRequired` | `false` | When `true`, a request the middleware would handle (its method is in `Methods` and `ShouldApply` accepts it) that carries no key gets 400 `g:idempotency_key_required` instead of running without idempotency. Set it per endpoint with `.RequireIdempotencyKey()`. |
| `Methods` | POST, PUT, PATCH, DELETE | HTTP methods that participate in idempotency. GET is never valid. |
| `InFlightStrategy` | `Reject` | `Reject` returns 409 on concurrent same-key requests. `WaitAndReplay` polls the durable store and replays the winner, or admits as a takeover once the winner's lease is lost. |
| `InFlightLockTimeout` | 30s | How long `WaitAndReplay` polls before giving up with 409 `g:idempotency_in_flight_timeout`. Validator-capped at 1 minute. |
| `InFlightLease` | 1 minute | How long the admitted attempt owns its key unless renewed. The middleware renews every third of this duration while the handler runs; only bounds how long a crashed attempt blocks its key before the next request can take it over. Must be between 1 second and 1 hour. |
| `MaxBodySizeForHashing` | 1 MiB | Maximum body size eligible for fingerprinting. Capped at 64 MiB. |
| `RequestBodyBufferThreshold` | 1 MiB + 1 byte | Request bytes retained in memory before buffering spills to a temporary file. Capped at 64 MiB + 1 byte. |
| `OversizeBehavior` | `Reject` | `Reject` returns 413 (`g:idempotency_body_too_large`). `PassThrough` runs the handler without idempotency guarantees. |
| `OnStoreError` | `Throw` | How the middleware reacts when the durable store throws before the handler runs, or while a `WaitAndReplay` request polls. `Throw` (default) propagates the exception as 5xx: a store that fails open silently drops the guarantee it exists for. `FailOpen` logs a warning and bypasses idempotency for the failing request. A completion or release failure after the response has started is always logged, never thrown, regardless of this setting. |
| `RequireUserIdentity` | `true` | When `true` and no `KeyDeriver` is set, requests without an authenticated user pass through. Set `false` for webhook receivers / OAuth callbacks: anonymous requests then share one namespace across all tenants, so give them a `KeyDeriver` with a verified per-sender discriminator. |
| `MismatchStatusCode` | 422 | Status code for fingerprint mismatch. Must be 409 or 422. |
| `ReplayHeaderAllowlist` | Content-Type, Content-Language, Content-Encoding, Content-Disposition, Location, Link, ETag, Last-Modified, Cache-Control, Vary | Response headers copied into the captured response. `Set-Cookie` and `traceparent` are excluded by design. |
| `ShouldCacheResponse` | `DefaultCachePredicate.Instance` | Predicate deciding whether a completed response is stored for replay. |
| `ShouldApply` | `null` | Per-request opt-out hook. To opt one endpoint out, use `[DisableResponseReplay]` or `.DisableResponseReplay()` instead. |
| `KeyDeriver` | `null` (uses `idem:{userId}:{METHOD}:{path}{?query}:{key}`) | Custom key-scope derivation, run instead of the `RequireUserIdentity` check; an empty return skips idempotency. The store key is the SHA-256 hex of whatever this returns; the store tenant still comes from the tenant claim. |
| `RequestFingerprint` | `null` (uses SHA-256 of buffered body) | Custom fingerprint computation. Must return non-empty bytes. |

### Runtime behavior

- Reads the authenticated `ICurrentUser.UserId` for the key scope and the principal's tenant claim for the store tenant (host scope when absent or anonymous); the ambient `ICurrentTenant` is ignored for keying. When `RequireUserIdentity` is set, no user is authenticated, and no `KeyDeriver` is configured, the middleware passes through without applying idempotency.
- Buffers the request body via `HttpRequest.EnableBuffering`; bytes beyond `RequestBodyBufferThreshold` spill to a temporary file.
- On replay, writes `Idempotent-Replayed: true` to the response. Pre-existing allowlisted response headers set by upstream middleware are removed before captured headers are written for byte-equivalent replay.
- The captured response is stored under the contract tag `headless.idempotency.response/v2`, with the body at its exact size. Read it back through `IHttpIdempotencyLookup`, not by decoding the stored bytes.
- On admission, sets `IHttpIdempotencyContext` as an `HttpContext` feature before invoking the handler, then renews the lease every third of `InFlightLease` while it runs; renewal stops before completion so it cannot race the settlement that ends the lease.
- Completion runs after the handler's unit commits, with `CancellationToken.None` so a client disconnect cannot strand the admission until its lease expires. A `StaleAdmissionException` there (the lease expired or was taken over while the handler ran) is logged, never thrown. Only the owning attempt's result is ever stored.
- When the **response** body exceeds `MaxBodySizeForHashing`, the body is not stored. For a response the cache predicate accepts (a success), the key still settles: it stores a `409` problem response with `g:idempotency_response_too_large`, which every retry replays, so the handler never runs a second time. A response the predicate refuses (a 5xx) releases the key as before. `OversizeBehavior` controls **request**-body handling only.
