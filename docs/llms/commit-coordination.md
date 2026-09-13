---
domain: Commit Coordination
packages: CommitCoordination.Abstractions, CommitCoordination.Core, CommitCoordination.EntityFramework, EntityFramework.CommitCoordination, CommitCoordination.PostgreSql, CommitCoordination.SqlServer
---

# Commit Coordination

> Commit Coordination runs registered work only after the unit of work it belongs to has durably committed, and discards it when that unit of work rolls back.

## Orientation

Use Commit Coordination when a framework subsystem must defer work until the data it belongs to has durably committed. Messaging uses it to store outbox rows inside the relational transaction and dispatch only after commit; Jobs uses it to write job rows inside the caller's transaction and wake the scheduler after commit. Work that must write rows inside that transaction reaches the live connection through `ICommitCoordinator.Relational`.

The contract is small and process-local. A scope is opened for one physical unit of work (`ICommitScopeFactory.Open`), consumers register callbacks on its coordinator (`OnCommit`), the owner signals the terminal outcome (`ICommitScope.SignalAsync`), and on commit the callbacks drain in registration order. Each callback runs once per coordinator instance, receives no cancellation token, and is lost if the process crashes before it runs; nothing is persisted and no sweep recovers it. Durable delivery therefore never comes from a callback: it comes from the row the consumer commits inside the transaction plus that consumer's own recovery sweep (the messaging relay, the jobs poller). A callback is only the fast path that dispatches such a row sooner.

Consumers map their own guarantee onto the coordination state. Messaging's `DeliveryMode` (per call, then per type via `WithDeliveryMode`, then `MessagingOptions.DefaultDeliveryMode`, default `Durable`) resolves as follows, and every throw happens before storage or transport effects; a scope is compatible when the consumer's storage can join its boundary (relational storage on the same database, in-memory storage in a scope with no relational handle):

| Requested mode | Compatible live coordinated scope | No scope | Incompatible scope |
|---|---|---|---|
| `Durable` (default) | capture in the caller's transaction, dispatch after commit | store first, the relay dispatches | throw |
| `Coordinated` | capture in the caller's transaction, dispatch after commit | throw | throw |
| `Direct` | transport now | transport now | transport now |

`Coordinated` is additionally rejected at Messaging startup when no `ICommitScopeFactory` is registered or when durable consumers run below the `Transactional` inbox tier. Jobs' `RequireAtomicEnlistment` behaves like `Coordinated`; without it a job write enlists in a compatible live relational transaction and otherwise inserts directly. See [Delivery Modes](messaging.md#delivery-modes) and [Commit-Coordinated Enqueue](jobs.md#commit-coordinated-enqueue-atomic-enqueue).

Two behaviors of this contract are documented, not defects. Commit callbacks are savepoint-blind: a callback registered inside a savepoint that is later rolled back still runs on the outer commit (details under [Core Design Notes](#design-notes-1)). The plain-`DbContext` `ExecuteCoordinatedTransactionAsync` helper runs under EF's execution strategy and replays the whole operation for a failure before `CommitAsync` starts, while the Headless save pipeline honors `CommitRetryGuard` and does not replay a participant write (details under the [EntityFramework Quick Start](#quick-start-2)).

Messaging's transactional inbox uses the application `DbContext` transaction to commit the fenced inbox outcome, enlisted application state, and captured durable Bus/Queue rows together. User code is never wrapped in transparent execution-strategy replay. Handler entry, direct transport, and external or otherwise non-enlisted effects can repeat.

## Agent Rules

- Consumer packages depend on `Headless.CommitCoordination.Abstractions`; provider packages depend on `Headless.CommitCoordination.Core`. Consumers never open scopes: they read `ICurrentCommitCoordinator.Current` and enlist on it when it is non-null.
- `Headless.CommitCoordination.EntityFramework` owns the EF interceptor path and the startup gate. `Headless.EntityFramework.CommitCoordination` is the separate adapter that makes the Headless save pipeline select it.
- Register work with `OnCommit(Func<ValueTask>)` only. There is no rollback callback: rollback discards the registrations and disposes scope-local state. Do not expose commit or rollback control to consumers.
- Use `GetOrAdd<TState>` only for scope-local state (a per-transaction buffer, a `CommitRetryGuard`). Do not use it as a service locator.
- Read `ICommitCoordinator.Relational` when work must write durable rows inside the active relational transaction. `null` means the scope is not bound to a relational transaction; work that must be durable fails closed on `null` instead of falling back to an in-memory buffer, unless the consumer already owns a durable store plus a recovery sweep.
- Never make correctness depend on a callback running. A callback is process-local, unrecoverable after a crash, and skipped when a provider signal is missed; back every callback with a durable row committed in-transaction plus an independent polling/recovery sweep.
- Prefer the single-call `ExecuteCoordinatedTransactionAsync(...)` helper over hand-rolling `BeginTransaction` + `EnlistCommitCoordination`; it welds the enlist (and, for raw ADO, the signal) into the transaction so nothing can be forgotten. A `HeadlessDbContext` self-sources its request scope (no `IServiceProvider` argument); a plain `DbContext`, `SqlConnection`, or `NpgsqlConnection` cannot, so those overloads require the scope passed explicitly. Pass the **request-scoped** provider (e.g. `HttpContext.RequestServices` or an injected scoped `IServiceProvider`), never the root container: the post-commit drain resolves scoped services.
- On SQL Server and PostgreSQL the signal is explicit. After a hand-rolled `EnlistCommitCoordination`, call `scope.SignalAsync(CommitOutcome.Committed)` (or `RolledBack`) as soon as the transaction completes, then dispose the scope. An un-signalled dispose is a rollback.
- Open and enlist from the frame that owns the unit of work, never from inside an `async` helper: the ambient coordinator lives in an `AsyncLocal` and a push made inside an `async` method does not flow back to its caller.
- Code that may register after an outcome (a callback that enlists more work, a late participant) checks `State == CommitCoordinatorState.Active` first or catches `InvalidOperationException`; the coordinator throws on registration after a terminal state.

## Core Concepts

### Coordinator

`ICommitCoordinator` is the register-only view of one scope. It exposes `State` (`Active`, `Committed`, `RolledBack`), `Relational`, `OnCommit`, and `GetOrAdd`. It accepts registrations only while `Active`; a registration after the terminal transition throws `InvalidOperationException`. It never decides the outcome, and it is an in-memory object: callbacks are process-local, run once per coordinator instance after the outcome is durable, receive no cancellation token, are not recovered after a crash, and drain in registration order. A callback fault does not stop the remaining callbacks; every registered callback runs and the faults surface to the signaller after the drain (one fault as-is, several as an `AggregateException`).

Every scope is an independent root. Opening a scope while another is ambient does not join it: the new coordinator has its own registrations and its own outcome, and the outer coordinator becomes ambient again once the inner scope is disposed. Disposing an outer scope while an inner one is still active throws.

### Scope Lifecycle

`ICommitScope` is the owner-side handle returned by `ICommitScopeFactory.Open(IRelationalCommitContext?)` and by every provider enlistment helper. Opening pushes the coordinator onto the ambient stack synchronously in the caller's frame; disposing pops it, also synchronously, so `await using` does not strand the `AsyncLocal`.

| Event | Effect |
|---|---|
| `Open(relational)` | New `Active` coordinator; ambient `Current` is the new coordinator immediately. |
| `SignalAsync(Committed)` | Claims `Committed` synchronously, then drains the callbacks in registration order and disposes scope-local state; the returned task completes when the drain has finished and faults after the drain if any callback faulted. |
| `SignalAsync(RolledBack)` | Claims `RolledBack`; callbacks are discarded, scope-local state is disposed, nothing runs. |
| Repeated signal, same outcome | Silent no-op. |
| Later signal, conflicting outcome (including after dispose) | Ignored and logged as a warning; the first claim stands. |
| `Dispose` / `DisposeAsync` without a signal | Rollback: registrations discarded, scope-local state disposed. `Dispose` runs that disposal in the background; `DisposeAsync` awaits it. A dispose that follows a signal is the normal lifecycle and logs nothing. |
| Dispose racing an in-flight commit drain | The commit claim wins; the dispose never rolls back committed work. |
| `SignalAsync(Unspecified)` | `ArgumentOutOfRangeException`. |

State transitions are one-way and atomic. `Committed` is observable before the drain has finished, so `State` alone does not prove the callbacks ran.

### Signalling

The scope owner signals the outcome; consumers never do. Who the owner is depends on the provider:

- **EF Core** (`Headless.CommitCoordination.EntityFramework`): the registered `CommitCoordinationTransactionInterceptor` signals on the transaction's commit or rollback edge. The synchronous EF edges claim the outcome on the committing thread and drain off-thread; the async edges await the drain. Drain faults are logged, never propagated to the committing caller. An explicit `SignalAsync` from the caller composes with it because signals are idempotent per outcome.
- **SQL Server and PostgreSQL** (raw ADO): neither package observes a commit edge, so the signal is explicit. `ExecuteCoordinatedTransactionAsync` signals for the caller (`Committed` after its own `CommitAsync`, `RolledBack` when the operation or commit throws); a caller that uses `EnlistCommitCoordination` directly owns the signal. An un-signalled dispose after the transaction has already completed logs a warning because the signal was almost certainly forgotten.
- **Non-relational units of work** (tests, in-memory storage): a provider or test fixture calls `ICommitScopeFactory.Open(null)` and signals the scope itself.

### Relational Handle

`IRelationalCommitContext` exposes the live `DbConnection` and `DbTransaction` the scope was opened with, surfaced as `ICommitCoordinator.Relational`. Outbox and job writers use it to place their rows inside the caller's transaction so the rows disappear with it on rollback. Both properties return `null` once the transaction has closed, so read them while the transaction is live, not from a post-commit callback. `Relational` itself is `null` for a scope that is not bound to a relational transaction.

### Scope-Local State

`GetOrAdd<TState>(factory)` creates at most one instance of each state type per coordinator. Construction is atomic under the coordinator's lock, so a factory that registers an `OnCommit` callback on construction never registers it twice. State that implements `IAsyncDisposable` or `IDisposable` is disposed after the terminal outcome, on commit (after the drain) and on rollback alike. `CommitRetryGuard` is the one state type the framework ships: a participant calls `PreventRetry()` before a write the unit-of-work owner cannot replay from its change tracker, and the owner reads `IsRetryPrevented` before retrying.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.CommitCoordination.EntityFramework` | EF Core owns the transaction (`DbContext`). | The unit of work is raw ADO. | The interceptor signals for you; EF must attach it (the generic `AddEntityFrameworkCommitCoordination<TContext>()` does) and the startup gate verifies that it fires. |
| `Headless.CommitCoordination.PostgreSql` | Raw `NpgsqlConnection` transactions. | EF owns the transaction (use the EF provider). | Explicit signal: the helper signals for you; hand-rolled enlistment must signal. No startup gate. |
| `Headless.CommitCoordination.SqlServer` | Raw `SqlConnection` transactions. | EF owns the transaction (use the EF provider). | Same explicit-signal contract as PostgreSQL. No diagnostics subscription, no hosted service. |

`Headless.EntityFramework.CommitCoordination` is not a provider; it is the adapter that makes the `Headless.EntityFramework` save pipeline enlist through the EF provider.

## Headless.CommitCoordination.Abstractions

### Problem Solved

Defines the public commit coordination contracts without provider dependencies.

### Key Features

- `ICommitCoordinator`: `State`, `Relational`, `OnCommit(Func<ValueTask>)`, and `GetOrAdd<TState>` (plus an allocation-free `GetOrAdd<TState, TArg>` overload).
- `ICurrentCommitCoordinator.Current`: the innermost active coordinator for the current async flow, or `null`.
- `ICommitScope`: the owner handle with `Coordinator` and `SignalAsync(CommitOutcome)`; disposable both ways.
- `ICommitScopeFactory.Open(IRelationalCommitContext?)`: the single scope-opening primitive used by provider helpers (hidden from IntelliSense).
- `IRelationalCommitContext`: the live `DbConnection` / `DbTransaction` handle.
- `CommitCoordinatorState` (`Active`, `Committed`, `RolledBack`) and `CommitOutcome` (`Unspecified = 0`, `Committed = 1`, `RolledBack = 2`); `Unspecified` is the default sentinel and is rejected by `ICommitScope.SignalAsync`.
- `CommitProbeMode` (`Disabled` / `Warn` / `Strict`): the shared reaction posture for a provider's startup self-probe.
- `CommitRetryGuard`: shared scope-local marker for participant writes that the unit-of-work owner cannot safely replay.

### Design Notes

The root contract is not a transaction. Consumers can register work but cannot decide the terminal outcome, and nothing they register is durable: callbacks are process-local, run once per coordinator instance after the physical outcome is durable, receive no cancellation token, are not recovered after a crash, drain in registration order, and a fault in one does not stop the rest. Nothing runs on rollback.

Participants obtain `coordinator.GetOrAdd(static _ => new CommitRetryGuard())` and call `PreventRetry()` before a non-replayable write attempt. The owner retains that instance and checks `IsRetryPrevented` even after scope disposal. The marker never resets. The Headless EF adapter observes it; other unit-of-work owners must explicitly honor it.

### Installation

```bash
dotnet add package Headless.CommitCoordination.Abstractions
```

### Quick Start

```csharp
using Headless.CommitCoordination;

var coordinator = currentCommitCoordinator.Current;

if (coordinator is { State: CommitCoordinatorState.Active })
{
    // Durable row first, inside the caller's transaction, when one is present.
    var transaction = coordinator.Relational?.Transaction;

    // Fast path only: dispatch the row sooner once the commit is durable.
    coordinator.OnCommit(() => ValueTask.CompletedTask);
}
```

### Configuration

None.

### Dependencies

None.

### Side Effects

None.

## Headless.CommitCoordination.Core

### Problem Solved

Implements the in-process coordinator, the ambient `AsyncLocal` stack, the scope factory, and the relational handle.

### Key Features

- Thread-safe callback registration and scope-local state under one coordinator lock.
- Ambient current coordinator through `ICurrentCommitCoordinator` (backed by an internal `AsyncLocal` stack); the push and pop run synchronously in the opening and disposing frames.
- Every `Open` is an independent root; a scope disposed out of order throws, and a frame whose parent already popped is ignored.
- Terminal claim is synchronous and first-wins; the drain runs callbacks in registration order without a cancellation token, disposes scope-local state on both outcomes, and surfaces callback faults after the drain (one as-is, several as an `AggregateException`).
- Signals are idempotent per outcome: a repeated same-outcome signal is silent, a conflicting one is ignored and logged (`CommitCoordinator`, event 1, warning).

### Design Notes

`Dispose` claims rollback for an un-signalled scope and runs the scope-local disposal in the background so sync callers are not blocked; a background disposal fault is logged (`CommitCoordinator`, event 2, error). `DisposeAsync` pops the ambient frame synchronously and then awaits the same disposal, so `await using` does not strand `AsyncLocal` state. A dispose after a signal claims nothing and logs nothing.

**Savepoints are invisible to the coordinator.** Enlisted work binds to the OUTERMOST commit edge only: a `RollbackToSavepoint` discards the database writes made after the savepoint but does NOT discard commit work registered during that window; on final commit, all registered work drains, including work registered inside the rolled-back region. If an operation publishes or enqueues inside a partial-rollback region, that mismatch is the consumer's to manage: enlist work only after the last possible partial rollback, or dispose the `OnCommit` registration handle while the coordinator is still active. Nested savepoint tracking is deliberately out of scope; so is scope joining, since every scope is its own root.

### Installation

```bash
dotnet add package Headless.CommitCoordination.Core
```

### Quick Start

```csharp
using Headless.CommitCoordination;

services.AddCommitCoordination();
```

Provider packages call this internally; call it directly only for a host that opens non-relational scopes itself.

### Configuration

None.

### Dependencies

- `Headless.Checks`
- `Headless.CommitCoordination.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

Registers `ICurrentCommitCoordinator` and `ICommitScopeFactory`; the backing stack, factory, coordinator, and relational-handle types are internal. Repeated calls are idempotent. `ICurrentCommitCoordinator` is registered unconditionally (guarded by an internal sentinel) so the real coordinator wins over the null fallback that `Headless.Messaging.Core` `TryAdd`s, whichever setup call the host invokes first.

## Headless.CommitCoordination.EntityFramework

### Problem Solved

Bridges EF Core's transaction commit/rollback edges to commit coordination, so work buffered inside a transaction — outbox dispatch, durable jobs — drains atomically on commit and is discarded on rollback. It also closes the interceptor-attach footgun (EF Core does not auto-discover DI-registered interceptors) and surfaces a mis-wire loudly at startup.

### Key Features

- Internal `CommitCoordinationTransactionInterceptor` owns the transaction-to-scope map and signals the enlisted scope on the commit/rollback edge; the map entry is evicted when the scope is disposed.
- `AddEntityFrameworkCommitCoordination<TContext>()` wires the registered context, commit interceptor, and startup probe. The nongeneric overload registers services only for advanced integrations.
- `DbContext.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call resilient coordinated transaction (plain `DbContext`; pass the request scope). `HeadlessDbContext` and `HeadlessIdentityDbContext` (any `IHeadlessDbContext`) have a scope-free overload in `Headless.EntityFramework.CommitCoordination`.
- `DatabaseFacade.EnlistCommitCoordination(transaction, services)` — the advanced seam for a transaction you already own; returns the `ICommitScope`, which the interceptor signals for you and which you may also signal explicitly (a repeated same-outcome signal is a silent no-op). Enlisting the same transaction twice throws.
- The generic helper auto-attaches only the commit-coordination interceptor through `IDbContextOptionsConfiguration<TContext>`, including plain `AddDbContext<TContext>` registrations. Repeated calls are idempotent.
- Internal startup gate `CommitInterceptorStartupGate<TContext>` with `CommitProbeMode` (`Disabled` / `Warn` / `Strict`, default `Warn`) configured through `CommitInterceptorProbeOptions`.

### Design Notes

EF Core does not auto-discover `IInterceptor` registrations from the application container. Use `AddEntityFrameworkCommitCoordination<TContext>()` after registering a plain application context: it attaches the commit interceptor to every options build and registers the startup probe. The nongeneric overload is a service-only seam for integrations that already own attachment and probing. The Jobs application-context provider convenience methods and the messaging EF storage path wire the generic stack automatically.

**The startup gate turns the silent mis-wire into a boot-time signal.** When coordination is enabled but the interceptor is not actually attached, a transaction *looks* transactional but isn't — publishes drain as rollback and vanish with no error. `CommitInterceptorStartupGate<TContext>` runs before any hosted service: it commits an empty transaction (no data mutated) on the consumer's `DbContext` and asserts the commit interceptor fired. On a mis-wire it logs a loud warning (`Warn`, the default) or throws at startup (`Strict`, opt-in via `services.Configure<CommitInterceptorProbeOptions>(o => o.Mode = CommitProbeMode.Strict)`). An unreachable database or unresolvable context is inconclusive, logged at debug, and lets the host start. The on-by-default `Headless.Messaging.Core` EF storage path enables this gate automatically; raw-ADO storage paths attach no interceptor and use the SqlServer/PostgreSql explicit-signal helpers instead.

**Explicit signals compose with the interceptor.** The interceptor claims the outcome synchronously on the commit thread and drains off-thread; the async EF edges await the drain. Drain faults are logged (`CommitCoordinationTransactionInterceptor`, event 2, error) and never propagated to the committing caller, because the outcome is already durable and a propagated fault would read as a phantom failure (and, under an execution strategy, replay the operation). A caller that must settle the outcome itself — the messaging EF inbox runners do, after a commit that threw client-side but was confirmed committed by a probe — calls `scope.SignalAsync(CommitOutcome.Committed)` on the scope returned by `EnlistCommitCoordination`. Signals are idempotent per outcome, so when both the interceptor and the caller signal, the work drains once and nothing is logged. Disposing the scope evicts the interceptor's map entry, so a transaction whose commit raised no interceptor event leaks nothing.

The probe opens a real (empty) transaction against the database on every host start. Set `Mode = CommitProbeMode.Disabled` to skip that round-trip — the escape-hatch for a cold-start latency budget or a boot environment where the database is not yet reachable. The cost is losing early mis-wire detection; durability is unaffected because the outbox row and relay sweep recover the work either way.

### Installation

```bash
dotnet add package Headless.CommitCoordination.EntityFramework
```

### Quick Start

`ExecuteCoordinatedTransactionAsync` is **the recommended path** — it welds open + enlist + commit into one call so the enlist cannot be forgotten; raw `EnlistCommitCoordination` is the advanced seam (the EF interceptor signals the commit edge, so no manual signal is needed, unlike the raw-ADO SqlServer/PostgreSql providers).

The EF execution strategy may replay failures that occur before commit starts. Once `CommitAsync` begins, the helper surfaces any exception without replay because the server may already have committed; callers should reconcile by a client-generated key or another durable idempotency key before deciding to retry the business operation.

```csharp
using Headless.CommitCoordination;
using Microsoft.EntityFrameworkCore;

services.AddDbContext<MyDbContext>(options => options.UseNpgsql(connectionString));
services.AddEntityFrameworkCommitCoordination<MyDbContext>();

// Open + enlist + commit in one call; publishes inside the operation drain atomically on commit.
await db.ExecuteCoordinatedTransactionAsync(
    async (context, ct) =>
    {
        await context.SaveChangesAsync(ct);
        await bus.PublishAsync(new OrderPlaced(orderId), ct);
    },
    services: requestServiceProvider
);
```

### Configuration

Configure `CommitInterceptorProbeOptions.Mode`: `Warn` by default, `Strict` to fail startup on a miswired interceptor, or `Disabled` to skip the startup database probe.

### Dependencies

- `Headless.Checks`
- `Headless.CommitCoordination.Core`
- `Microsoft.EntityFrameworkCore.Relational`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions` — required by the startup gate (`CommitInterceptorStartupGate<TContext>`)
- `Microsoft.Extensions.Logging.Abstractions` — required by the startup gate
- `Microsoft.Extensions.Options` — required by `CommitInterceptorProbeOptions`

### Side Effects

Both overloads register core commit coordination and the transaction interceptor (as itself and as `IInterceptor`). The generic overload additionally attaches that interceptor to the selected context and registers an empty-transaction startup probe. It creates no schema and does not automatically start or enlist application transactions; use `ExecuteCoordinatedTransactionAsync` for the operation boundary.

## Headless.EntityFramework.CommitCoordination

This opt-in adapter connects `Headless.EntityFramework`'s internal save-pipeline transaction seam to `Headless.CommitCoordination.EntityFramework`. Install it and chain `.AddCommitCoordination()` from `AddHeadlessDbContextServices(...)` when buffered work must enlist in the transaction opened by the Headless save pipeline. The core `Headless.EntityFramework` package otherwise keeps a no-op coordinator and carries no commit-coordination package reference. `Headless.EntityFramework.Messaging` installs it automatically for its transactional outbox bridge.

```csharp
services
    .AddHeadlessDbContextServices()
    .AddCommitCoordination();
```

The adapter enlists through `DatabaseFacade.EnlistCommitCoordination` (the EF interceptor signals the outcome) and reads the scope's `CommitRetryGuard`. It also ships the scope-free `ExecuteCoordinatedTransactionAsync` overloads for any `IHeadlessDbContext` (`HeadlessCoordinatedTransactionExtensions`), which source the request scope from the context.

Coordinated Jobs write attempts prevent automatic retries of a pipeline-owned save because their separate context is not retained in the business change tracker. A later failure propagates unchanged; recover with a fresh context and aggregate graph after a known rollback, or reconcile an unknown commit first. Outbox-only saves retain their existing retry behavior.

## Headless.CommitCoordination.PostgreSql

### Problem Solved

Enlists raw-ADO `NpgsqlConnection` transactions in commit coordination so work buffered inside the transaction — outbox dispatch, durable jobs — drains after commit and is discarded on rollback.

### Key Features

- `NpgsqlConnection.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call coordinated transaction for raw ADO: opens the connection if closed, begins the transaction, enlists, runs the operation, commits, and signals the outcome for you (no execution-strategy retry).
- `NpgsqlConnection.EnlistCommitCoordination(transaction, services)` — the advanced seam for a transaction you already own; returns the `ICommitScope` you must signal.
- DI extension `AddPostgreSqlCommitCoordination()` (parameterless; there are no provider options).

### Design Notes

PostgreSQL signaling is **explicit**: Npgsql exposes no commit edge, so nothing signals for you. `ExecuteCoordinatedTransactionAsync` signals `Committed` after its own `CommitAsync` and `RolledBack` when the operation or commit throws. A caller that uses `EnlistCommitCoordination` directly owns that signal — call `scope.SignalAsync(CommitOutcome.Committed)` (or `RolledBack`) immediately after the transaction completes, then dispose the scope.

An un-signalled dispose is a rollback: the enlisted work is discarded. When the transaction had already completed by the time the scope is disposed without a signal, the package logs a warning (`PostgreSqlCommitScope`, event 1) because the signal was almost certainly forgotten — durable outbox rows are still relay-recovered, but the fast-path dispatch was lost. An un-signalled dispose while the transaction is still open (the operation threw before commit) is the normal failure path and logs nothing.

A callback fault after a successful commit is logged by the helper (`Headless.CommitCoordination.PostgreSql.CoordinatedTransaction`, event 1, error) and the operation's result is returned; surfacing it would invite a retry that double-applies an already-durable transaction. With direct enlistment the fault surfaces from `SignalAsync` instead.

The same contract applies to `Headless.CommitCoordination.SqlServer`. Prefer `Headless.CommitCoordination.EntityFramework` where EF owns the commit edge — its interceptor signals for the caller.

### Installation

```bash
dotnet add package Headless.CommitCoordination.PostgreSql
```

### Quick Start

`ExecuteCoordinatedTransactionAsync` is **the recommended path** — it welds open + enlist + commit + signal into one call so nothing can be forgotten:

```csharp
using Headless.CommitCoordination;
using Npgsql;

services.AddPostgreSqlCommitCoordination();

await connection.ExecuteCoordinatedTransactionAsync(
    async (conn, ct) => {
        // raw-ADO work on conn, plus publishes that enlist on the ambient coordinator
    },
    services: requestServiceProvider
);
```

#### Advanced: raw enlistment

```csharp
await using var tx = await connection.BeginTransactionAsync(ct);
await using var scope = connection.EnlistCommitCoordination(tx, requestServiceProvider);

// ... raw-ADO work + publishes ...
await tx.CommitAsync(ct);
await scope.SignalAsync(CommitOutcome.Committed); // REQUIRED — nothing signals for you
```

### Configuration

None.

### Dependencies

- `Headless.Checks`
- `Headless.CommitCoordination.Core`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `Npgsql`

### Side Effects

Registers the core commit coordination services only.

## Headless.CommitCoordination.SqlServer

### Problem Solved

Enlists raw-ADO `SqlConnection` transactions in commit coordination so work buffered inside the transaction — outbox dispatch, durable jobs — drains after commit and is discarded on rollback.

### Key Features

- `SqlConnection.ExecuteCoordinatedTransactionAsync(operation, services, …)` — single-call coordinated transaction for raw ADO: opens the connection if closed, begins the transaction, enlists, runs the operation, commits, and signals the outcome for you (no execution-strategy retry).
- `SqlConnection.EnlistCommitCoordination(transaction, services)` — the advanced seam for a transaction you already own; returns the `ICommitScope` you must signal.
- DI extension `AddSqlServerCommitCoordination()` (parameterless; there are no provider options).

### Design Notes

SQL Server signaling is **explicit**: this package observes no commit edge, so nothing signals for you. `ExecuteCoordinatedTransactionAsync` signals `Committed` after its own `CommitAsync` and `RolledBack` when the operation or commit throws. A caller that uses `EnlistCommitCoordination` directly owns that signal — call `scope.SignalAsync(CommitOutcome.Committed)` (or `RolledBack`) immediately after the transaction completes, then dispose the scope.

An un-signalled dispose is a rollback: the enlisted work is discarded. When the transaction had already completed by the time the scope is disposed without a signal, the package logs a warning (`SqlServerCommitScope`, event 1) because the signal was almost certainly forgotten — durable outbox rows are still relay-recovered, but the fast-path dispatch was lost. An un-signalled dispose while the transaction is still open (the operation threw before commit) is the normal failure path and logs nothing.

A callback fault after a successful commit is logged by the helper (`Headless.CommitCoordination.SqlServer.CoordinatedTransaction`, event 1, error) and the operation's result is returned; surfacing it would invite a retry that double-applies an already-durable transaction. With direct enlistment the fault surfaces from `SignalAsync` instead.

The same contract applies to `Headless.CommitCoordination.PostgreSql`. Prefer `Headless.CommitCoordination.EntityFramework` where EF owns the commit edge — its interceptor signals for the caller.

### Installation

```bash
dotnet add package Headless.CommitCoordination.SqlServer
```

### Quick Start

`ExecuteCoordinatedTransactionAsync` is **the recommended path** — it welds open + enlist + commit + signal into one call so nothing can be forgotten:

```csharp
using Headless.CommitCoordination;
using Microsoft.Data.SqlClient;

services.AddSqlServerCommitCoordination();

await connection.ExecuteCoordinatedTransactionAsync(
    async (conn, ct) => {
        // raw-ADO work on conn, plus publishes that enlist on the ambient coordinator
    },
    services: requestServiceProvider
);
```

#### Advanced: raw enlistment

```csharp
await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
await using var scope = connection.EnlistCommitCoordination(tx, requestServiceProvider);

// ... raw-ADO work + publishes ...
await tx.CommitAsync(ct);
await scope.SignalAsync(CommitOutcome.Committed); // REQUIRED — nothing signals for you
```

### Configuration

None.

### Dependencies

- `Headless.Checks`
- `Headless.CommitCoordination.Core`
- `Microsoft.Data.SqlClient`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

Registers the core commit coordination services only. It starts no hosted service and subscribes to no diagnostics.
