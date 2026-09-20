---
domain: Unit of Work
packages: UnitOfWork.Abstractions, UnitOfWork, UnitOfWork.EntityFramework, UnitOfWork.PostgreSql, UnitOfWork.SqlServer
---

# Unit of Work

> A scoped, explicitly-begun unit of work that lets Messaging and Jobs enlist durable writes in the caller's transaction and defer dispatch until it commits — with no `AsyncLocal`, no capture-before-await rule, and no service-provider argument to thread through.

## Orientation

An application developer opens a unit of work on the line they choose, does business work, publishes messages through the unit and enqueues jobs through the managers they already inject, and completes it; everything enlisted inside is atomic with the transaction and dispatches after commit. The entry point is `IUnitOfWorkManager` (scoped), resolved like any other scoped service. Its `Current` property is a plain field — not an `AsyncLocal` — so there is no capture-before-first-await discipline to get wrong: whatever is resolved from the same DI scope sees the same `Current`, regardless of when during an `async` method the unit was begun.

`IUnitOfWorkManager.BeginAsync(...)` opens a resource-less coordination window; provider packages add resource-bearing overloads — `BeginAsync(db, ...)` for EF Core, `BeginAsync(connection, ...)` for raw ADO — that begin the transaction on that line (**owned mode**) and return an `IUnitOfWork` handle. `IUnitOfWork.CompleteAsync` commits the resource, then drains registered post-commit work in order; disposing without completing is an implicit rollback. `Enlist(resource, transaction)` is the advanced seam for code that already owns its commit edge (the Headless save pipeline, the messaging inbox runners) — **observed mode** — where the caller commits the transaction itself and `CompleteAsync` only drains.

Pick a provider by what owns the transaction:

- EF Core owns it (`DbContext`) → `Headless.UnitOfWork.EntityFramework`.
- Raw ADO on PostgreSQL → `Headless.UnitOfWork.PostgreSql`.
- Raw ADO on SQL Server → `Headless.UnitOfWork.SqlServer`.
- No relational resource at all (a script spanning several independent saves, a test harness) → the resource-less core member on `Headless.UnitOfWork` itself.

The two participant domains reach a unit of work differently, and the difference is visible at the call site:

- **Messaging enlists through the unit, never through the publisher.** `IBus` and `IQueue` are autonomous singletons: they never read `IUnitOfWorkManager.Current`, and a publish through them writes a standalone durable row that survives the caller's rollback. To write a message inside the transaction, publish through the unit itself — `unit.Outbox.PublishAsync(...)` / `unit.Outbox.EnqueueAsync(...)`, an accessor that `Headless.Messaging.Abstractions` adds to `IUnitOfWork`. That surface always enlists and refuses, before any effect, when the configured storage cannot join the unit. See [messaging.md § Delivery Modes](messaging.md#delivery-modes).
- **Jobs enlists through its scoped managers.** `ITimeJobManager<>`, `ICronJobManager<>`, and `IJobScheduler` are scoped facades that read `IUnitOfWorkManager.Current` at call time and enlist according to `TransactionEnlistment` — no extra parameter. See [Guarantee Matrix](#guarantee-matrix) for when a job write enlists, writes autonomously, or throws.

See [Choosing a Provider](#choosing-a-provider) for the package-selection table.

## Agent Rules

- Resolve `IUnitOfWorkManager` (or a scoped facade over it — a job manager) from a DI scope, never from the root provider. A scope models one operation; a singleton or hosted service that needs one creates its own scope (`IServiceScopeFactory.CreateScope()`). A host with `ValidateScopes` enabled turns a captive resolution into a startup-time error — the correct signal, not a bug to work around. `IBus` and `IQueue` are **not** in this set: they are autonomous singletons, so a singleton may inject them directly, and doing so buys no enlistment.
- To publish a message inside the transaction, call `unit.Outbox` on the handle you hold — not `IBus`/`IQueue` from any scope. Read the accessor at the call site rather than storing the binding: liveness is checked per publish against the handle it was read from, so a retained binding whose nested view has since completed throws on its next use, before anything is stored.
- Open the unit of work explicitly, on the line you choose. Nothing in this framework opens one on your behalf — no mediator behavior, no endpoint filter, no consumer-runtime wrapper. If a handler needs one, call `unitOfWorkManager.BeginAsync(...)` or `unitOfWorkManager.RunAsync(...)` yourself.
- There is no capture-before-first-await rule. Unlike the ambient design this replaced (see [Core Concepts § Why scoped, not ambient](#why-scoped-not-ambient)), `Current` is a plain field on a scoped object — begin it wherever is convenient in an `async` method; everything resolved from the same scope sees it.
- One resource per scope. Beginning again on the *same* resource while a unit is active joins it (returns a child handle); a *different* resource while a resource-bearing unit is active throws. Run unrelated transactional work in its own scope, not nested calls on the same manager.
- `Enlist(...)` is the advanced seam, not the default. Reach for `BeginAsync(...)` first — it begins the transaction and owns the commit. Use `Enlist` only when something else already owns the commit edge and you need the drain to piggyback on it (this is how the Headless save pipeline and the messaging inbox runners use it internally; most application code never calls it).
- `OnCompleted` callbacks are a fast path, never the durability mechanism. They are process-local, run once, receive no cancellation token, and are lost on a crash before they run. Durable delivery is the row committed in the transaction plus the consumer's own recovery sweep (the messaging relay, the jobs poller); a callback only dispatches that row sooner. Never make correctness depend on one running.
- Use `OnFailed` only to release a non-transactional resource reserved in anticipation of commit (a lock, a reservation) — not as a substitute for a proper rollback-safe design. Its faults are logged, never propagated.
- The manager is the only receiver that opens a unit of work. `BeginAsync`, `Enlist`, and `RunAsync` are extension members on `IUnitOfWorkManager`; no context, connection, or helper type carries a second spelling, and none of them take a `services:` parameter. Do not resolve `IServiceProvider` and thread it through a "coordinated transaction" helper — that pattern is gone.
- Under a retrying EF execution strategy, `BeginAsync(db)` throws by design — a user-initiated transaction cannot survive a strategy replay. Use `unitOfWorkManager.RunAsync(db, ...)`, which runs begin → operation → complete *inside* the strategy and only lets a failure replay before the commit has started.
- Replay re-runs the block that owns the unit, so an enlisted publish or Jobs write inside your own `RunAsync(db, …)` block leaves it replayable: a transient failure anywhere in the block replays it with a fresh unit, the first attempt's rows roll back, and the replayed block writes them again. The one place an enlisted write ends replay is an *observed-mode* unit — the `HeadlessDbContext` save pipeline's own save, from a domain-event handler, which the pipeline replays without re-running the handler. There both the publish and the Jobs write call `IUnitOfWork.PreventRetry()` before writing; after it the fault is surfaced outside the strategy, so reconcile an ambiguous post-commit fault with a durable idempotency key instead of retrying blind. The EF integration-event dispatcher is exempt even there: it marks the occurrences the save pipeline re-publishes on a replayed attempt.
- Banned: reading Jobs' `TransactionEnlistment` on a call and manually branching on whether a transaction is present. The framework's guarantee matrix (below) already encodes every combination and throws with a message naming the fix; hand-rolled branching duplicates and can drift from it.
- Do not compare `IUnitOfWork.State` to decide whether a handle can still carry work. A nested view forwards `State` to the unit it views, so a view that has already completed still reports `Active` while the root stays open. Attach work through a registration (`OnCompleted`, `OnFailed`, `GetOrAdd`) instead — each handle refuses one it can no longer carry.
- `GetFeature<TFeature>()` is a bridge-package seam, not an application API. Use the typed accessor a bridge ships (`unit.Outbox`); register a feature (a service implementing `IUnitOfWorkFeature`) only when writing such a bridge.

## Core Concepts

### Scoped manager, not ambient

`IUnitOfWorkManager` is a scoped DI service. `Current` is a plain field it maintains — no `System.Threading.AsyncLocal<T>` exists anywhere in these packages. A unit of work is therefore visible to everything resolved from the same DI scope, from the moment it is begun, with no dependency on *when* during an `async` method the begin happened or which `await` boundaries separate the begin from a later read of `Current`.

#### Why scoped, not ambient

The design this replaced pushed a scope onto an `AsyncLocal` stack; because `AsyncLocal` mutations made inside an `async` callee do not flow back to the caller (`ExecutionContext` is copy-on-write and restored on return), the ambient design needed every provider to open its scope **synchronously, in the caller's own stack frame** — a discipline enforced only by comments and XML prose, and one that produced a real, hard-to-see bug: an `async` enlist helper set the ambient scope correctly inside itself, but the caller read `Current` back as `null`, silently turning a transactional outbox write into a non-atomic one while the happy-path test stayed green (`docs/solutions/logic-errors/asynclocal-ambient-scope-stranded-across-await.md`). A scoped manager has no such failure mode: `Current` is a field on an object every consumer in the scope already resolves through DI, so there is nothing to strand across an await. This mirrors precedent: MassTransit's `ScopedConsumeContextProvider` and Wolverine's `ScopedMessageContextHolder` (adopted specifically to fix the same class of ambient-propagation bug, GH-2583/GH-3001) both moved from `AsyncLocal` to a scoped field for the same reason.

The cost of this design is explicit: a singleton or a hosted service that needs `IUnitOfWorkManager` or a scoped facade over it (a job manager) must create its own scope. Resolving one from the root container under `ValidateScopes` throws — by design, this is the framework surfacing a captive-dependency mistake rather than silently handing back a root-scoped instance.

Messaging's publishers are deliberately outside that cost. `IBus` and `IQueue` hold no scope-bound state, so they are singletons a framework singleton can depend on directly; enlistment moved to the unit-of-work outbox, reached from the unit rather than from the publisher. A singleton that needs to publish therefore needs no scope at all, and creating one would not make its publish transactional.

### Owned vs observed mode

Owned vs observed is a flag on the enlisted `IUnitOfWorkResource`, never a second handle type — `IUnitOfWork` looks the same either way.

- **Owned mode** (`BeginAsync(...)`): the unit begins the transaction itself, on that line, and owns the commit. `CompleteAsync` commits the resource, then drains `OnCompleted` registrations, then disposes scope-local state. A dispose without `CompleteAsync` rolls the transaction back.
- **Observed mode** (`Enlist(resource, transaction)`): the caller already owns and commits the transaction. The resource's `CommitAsync`/`RollbackAsync` are no-ops; `CompleteAsync` only drains. The caller must call `CompleteAsync` after its own commit, or `RollbackAsync` after its own rollback — a dispose that follows neither, after the transaction has already finished, is logged as a forgotten completion (the durable rows are still relay-recovered; only the fast-path drain was lost).

There is one commit verb regardless of mode: `IUnitOfWork.CompleteAsync`. There is no third, interceptor-driven mode — the Headless save pipeline and the messaging inbox runners already know their own commit outcome and use observed mode to tell the unit about it.

### Nesting: join by default

Beginning a unit of work while one is already active in the scope does not always open a second root:

| Situation | Result |
|---|---|
| Same resource already active | A **child** handle over the same engine. The child's `CompleteAsync` transfers its registrations to the root instead of draining them itself; a child disposed without completing drops its registrations and marks the root aborted, so the root's own `CompleteAsync` throws naming the abandoned child. |
| Resource-less root, resource-bearing begin | An **independent nested unit** with its own commit and drain; its registrations are not transferred to the root. This is the shape of `BeginAsync()` followed by a `HeadlessDbContext.SaveChangesAsync()` that opens its own transaction, and the shape a test harness's `RunInUnitOfWorkAsync` uses. |
| A *different* resource while a resource-bearing unit is active | Throws — one physical resource per scope; run the second operation in its own service scope. |
| A concurrent `BeginAsync`/`Enlist` while another begin is in flight in the same scope | Throws — the manager claims its slot synchronously before the first `await`, so two begins racing in one scope fail deterministically instead of silently producing two roots. |

`Current` always returns the innermost active frame: the child while it is active, the outer child (or the root) again once the inner child completes.

### The `DbContext` binding and adoption

`Headless.UnitOfWork.EntityFramework`'s `BeginAsync(db)` and `Enlist(db, tx)` additionally record a binding from the `DbContext` instance to the unit (a `ConditionalWeakTable`, cleared once the unit reaches a terminal state). This exists because a context created through `IDbContextFactory<T>` owns its *own* DI scope — its manager is not the same object as the scope that resolved the factory. The Headless save pipeline (`Headless.EntityFramework`) consults this binding *before* its own scope's `IUnitOfWorkManager.Current`; when the bound unit belongs to a different scope's manager, the pipeline **adopts** it into its own scope for the save's duration (the hidden `IUnitOfWorkManager.Adopt(IUnitOfWork)`, swap-and-restore, re-entrant when the slot already holds that same unit), so domain-event handlers and the outbox dispatcher resolved in that scope see the same `Current`. Application code never calls `Adopt` directly — it exists so a factory-created context's transaction is still visible to everything the save touches.

### The failure hook and rollback

`IUnitOfWork.OnFailed(Func<UnitOfWorkFailure, ValueTask>)` runs after the unit reaches its failed terminal state — an explicit `RollbackAsync`, a dispose without completing (abandon), a commit fault, a child abandon aborting the root, or the owning scope disposing while the unit was still active. `UnitOfWorkFailure.Reason` is one of `RolledBack`, `Abandoned`, `Faulted`, `ScopeDisposed`, or `ChildAbandoned`; `Exception` carries the originating fault when one exists. Failure-hook faults are logged and never propagated — a cleanup callback must not be able to mask or replace the real failure.

`RollbackAsync()` is idempotent and legal in both modes: in owned mode it rolls the resource back; in observed mode it records that the caller's own transaction rolled back (and, like `CompleteAsync`, suppresses the forgotten-completion warning). A commit fault transitions the unit to `Failed` *before* the exception propagates, so a second `CompleteAsync` throws the "already failed" message rather than attempting to re-commit — this is the fix for a class of bug where a caller retries a commit whose outcome is actually unknown.

### Typed features on a unit (`GetFeature`)

A bridge package can expose behavior on a unit of work that the unit-of-work packages know nothing about, without either side referencing the other. It registers an ordinary service that implements the `IUnitOfWorkFeature` marker, and `IUnitOfWork.GetFeature<TFeature>()` resolves that type from the scope that owns the unit's manager, returning `null` when the host registered none. The marker is the whole gate: only opted-in types resolve, so the unit is not a service locator.

- A lookup, not a registration: nothing is created or cached per unit, a nested view resolves the same instance as its root, and a completed view still resolves it. A feature therefore holds no unit; the handle it is used with arrives as an argument per call, and that call's first registration on the handle is what answers for the handle's liveness.
- `GetFeature` throws `ObjectDisposedException` on a disposed handle and nothing else. A manager constructed outside DI resolves no features.

The one first-party feature today is Messaging's enlisted outbox: `AddHeadlessMessaging` registers the singleton `IUnitOfWorkOutbox`, and `Headless.Messaging.Abstractions` surfaces it as the `unit.Outbox` accessor.

### Handle liveness: registrations answer for the view

`State` is the *unit's* lifecycle, and a nested view forwards it to the unit it views. A child view that has already completed therefore still reports `Active`, because the unit it completed into is still open — reading `State` to decide whether work can be attached to a handle gives the wrong answer for exactly that case. Registrations answer for the view instead: `OnCompleted`, `OnFailed`, and `GetOrAdd` on a view that already completed throw `InvalidOperationException` even while the root stays open, and every member throws `ObjectDisposedException` after the handle is disposed. An enlisted publish relies on this — the outbox writer's first act on the caller's handle is a registration, so a dead handle is refused before any row is stored.

### `TransactionEnlistment` and the Jobs guarantee matrix

`TransactionEnlistment { WhenAvailable = 0 (default), Required = 1, Never = 2 }`, in `Headless.UnitOfWork.Abstractions`, states how eagerly a **Jobs** write requires an active unit of work, resolved **per call > per function > host default**. It is Jobs-only. Messaging once shared it; enlistment there is now the receiver (`IBus`/`IQueue` versus `unit.Outbox`), so nothing in Messaging reads this enum, and `MessagingOptions.DefaultEnlistment`, the per-type `WithEnlistment(...)` policy, and the per-call `MessageOptions.Enlistment` are deleted. The enum itself stays, unchanged, for Jobs.

#### Guarantee Matrix

| `TransactionEnlistment` (Jobs) | Active unit of work, joinable compatible resource | No unit of work, or a unit with no joinable resource | Unit of work with an incompatible resource |
|---|---|---|---|
| `WhenAvailable` (default) | Write happens on the unit's transaction; dispatch/scheduler-restart/notification deferred to after commit | Autonomous durable write; the poller picks it up | Throws |
| `Required` | Same as above | Throws | Throws |
| `Never` | Autonomous — never enlists, even though a resource is active | Autonomous | Autonomous — never inspects the resource |

A Jobs write needs a *relational* resource on the *same database* to enlist into, so a resource-less unit counts as "no joinable resource" and behaves like no unit of work. An incompatible resource (a different database, a closed or completed transaction) throws for both `WhenAvailable` and `Required` — the framework never silently downgrades a requested enlistment. Composition across host/function/call tiers is strictest-wins (`Required` > `WhenAvailable` > `Never`); see `jobs.md`.

Messaging's counterpart is not a matrix over this enum: an enlisted publish is refused whenever the configured storage cannot join the given unit, and an autonomous publish never consults a unit at all. See `messaging.md#delivery-modes`.

### Message catalogue

Every illegal transition throws with a message naming the remedy — never a bare `InvalidOperationException`:

| Condition | Message |
|---|---|
| `BeginAsync(db)` with an existing transaction on the context | `The DbContext already has an active transaction. Begin the unit of work before beginning the transaction, or call IUnitOfWorkManager.Enlist(db, transaction) for a transaction you commit yourself.` |
| `BeginAsync(db)` under a retrying execution strategy | EF's own text, plus: `Use IUnitOfWorkManager.RunAsync(db, …) to run the unit of work as a retriable block.` |
| `CompleteAsync` after `Completed` | `The unit of work has already completed. Begin a new unit of work for further work.` |
| `CompleteAsync` after `Failed` | `The unit of work has already failed ({Reason}) and cannot be completed. Begin a new unit of work.` |
| `OnCompleted`/`OnFailed`/`GetOrAdd` after a terminal state | `The unit of work is {State}; registrations are accepted only while it is Active.` |
| `OnCompleted`/`OnFailed`/`GetOrAdd` (so also an enlisted publish) on a nested view that already completed | `This nested unit of work has already completed. Use the unit of work it was begun under, or begin a new one.` |
| A member call after dispose | `ObjectDisposedException("UnitOfWork")` |
| A second `BeginAsync`/`Enlist` on a *different* resource | `A unit of work is already active on another resource in this scope. Complete it first, or run the second operation in its own service scope (IServiceScopeFactory.CreateScope()).` |
| `BeginAsync`/`Enlist` while another begin is in flight in the same scope | `Another unit of work is being begun concurrently in this scope. Await the first BeginAsync before beginning again, or run parallel work in separate service scopes.` |
| A save on a context bound to another scope's unit while this scope already has a different active unit | `The unit of work bound to this DbContext belongs to another scope, and this scope already has a different active unit of work. Save the context inside the scope that began its unit of work, or complete this scope's unit first.` |
| Root `CompleteAsync` while a child is still active | `A nested unit of work begun in this scope is still active. Complete or dispose it before completing the root.` |
| Root `CompleteAsync` after a child was abandoned | `A nested unit of work was disposed without completing, so the root cannot complete; the transaction is rolled back.` |
| A caller-owned transaction saved with integration events but no unit bound to the context (or a resource-less bound unit) | `SaveChanges ran inside a caller-owned transaction that no unit of work owns, so integration events and jobs would dispatch non-atomically. Begin the unit of work on this context (IUnitOfWorkManager.BeginAsync(db)) before beginning the transaction, or enlist the transaction with Enlist(db, transaction).` |
| `unit.Outbox` in a host that never called `AddHeadlessMessaging` | `No messaging outbox is registered for this unit of work. Call AddHeadlessMessaging during startup to register it.` |
| An enlisted publish into a unit the messaging storage cannot join | `Publishing '{MessageType}' cannot join the active unit of work ({Mismatch}): {detail}. {advice}` — for example `… (MissingRelationalCapability): the active unit of work exposes no relational resource for the messaging storage to write into. Begin the unit of work over a relational resource for the messaging database, or publish without coordination.` |
| Scheduling a job with `TransactionEnlistment.Required` and no active unit of work | `Scheduling '{Function}' requires an active unit of work (TransactionEnlistment.Required) but none is active in this scope. Begin one with IUnitOfWorkManager.BeginAsync, or register the function with TransactionEnlistment.WhenAvailable.` |
| An incompatible or dead resource (Jobs) | `The active unit of work's transaction belongs to another database or is no longer live (closed, completed, or changed), so the Jobs write cannot enlist. Use the same database, or TransactionEnlistment.Never for this call.` |
| A relational unit of work active, but the configured Jobs provider can't write inside it | `An active unit of work has a joinable relational resource but the configured job persistence provider does not support coordinated writes. The coordinated-enqueue path requires the EF Core operational store (UseEntityFramework).` |
| Scope disposed with a unit still active | `A unit of work begun with BeginAsync was still active when its service scope was disposed; it was rolled back. Complete or dispose every unit of work before the scope ends.` (warning, `OnFailed` runs with `Reason = ScopeDisposed`) |
| Observed unit disposed without `CompleteAsync`/`RollbackAsync` after its transaction finished | `A unit of work enlisted with Enlist(...) was disposed without CompleteAsync or RollbackAsync after its transaction completed; the after-commit work was discarded and durable rows will be recovered by the relay.` (warning) |

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
|---|---|---|---|
| `Headless.UnitOfWork.EntityFramework` | EF Core owns the transaction (`DbContext`). | The unit of work is raw ADO. | `BeginAsync(db)` cannot run under a retrying execution strategy — use `RunAsync(db, …)` there. Never references `Headless.EntityFramework` (the dependency flows the other way), so it stays usable by any EF consumer. |
| `Headless.UnitOfWork.PostgreSql` | Raw `NpgsqlConnection` transactions. | EF owns the transaction (use the EF provider). | No commit edge to observe: observed mode is fully explicit — the caller must call `CompleteAsync`/`RollbackAsync` itself, or a forgotten completion is logged. No execution-strategy retry (a raw-ADO concept has none). |
| `Headless.UnitOfWork.SqlServer` | Raw `SqlConnection` transactions. | EF owns the transaction (use the EF provider). | Same explicit-completion contract as PostgreSQL. |
| The resource-less core (`Headless.UnitOfWork`, no provider) | A script spanning several independent transactional saves that should still share one commit-drain edge; a test harness. | Any case that needs a joinable relational resource — a resource-less unit only lets a *relational* provider join by opening an *independent nested unit*, not by sharing the same transaction. | No relational write enlists directly on it; each resource-bearing operation underneath opens and commits its own transaction. Messaging's in-memory storage is the one participant that can join it, through its buffer — which is what the test harness relies on. |

---

## Headless.UnitOfWork.Abstractions

Defines the public unit-of-work contracts without provider dependencies: the scoped manager entry point, the unit handle, the resource seams, the `IUnitOfWorkFeature` marker bridge packages opt into, and Jobs' `TransactionEnlistment` knob.

### API and behavior

- `IUnitOfWorkManager` (scoped): `Current`, the resource-less `BeginAsync(options?, ct)`, plus the provider primitives — the resource-factory `BeginAsync` (hidden from IntelliSense), observed-mode `Enlist` (hidden), and swap-and-restore `Adopt` (hidden).
- `IUnitOfWork`: `State`, `Failure`, `Resource`, `OnCompleted(Func<ValueTask>)`, `OnFailed(Func<UnitOfWorkFailure, ValueTask>)`, `GetOrAdd<TState>` (both overloads), `GetFeature<TFeature>()`, `PreventRetry()` / `IsRetryPrevented`, `CompleteAsync(ct)`, idempotent `RollbackAsync()`, dispose both ways.
- `IUnitOfWorkFeature`: the marker a bridge's feature service implements so `GetFeature` can hand it out. See [Typed features on a unit](#typed-features-on-a-unit-getfeature).
- `IUnitOfWorkResource` (`IsOwned`, `IsTransactionCompleted`, `CommitAsync`, `RollbackAsync`) and `IRelationalUnitOfWorkResource` (`Connection`, `Transaction`, non-null while active).
- `UnitOfWorkState` (`Active = 0`, `Completed = 1`, `Failed = 2`); `UnitOfWorkFailure` with `UnitOfWorkFailureReason` (`Unspecified`, `RolledBack`, `Abandoned`, `Faulted`, `ScopeDisposed`, `ChildAbandoned`).
- `UnitOfWorkOptions`: intentionally empty today; propagation knobs (`RequiresNew`/`Suppress`) land here additively.
- `TransactionEnlistment { WhenAvailable = 0, Required = 1, Never = 2 }`: Jobs only. See [Guarantee Matrix](#guarantee-matrix).

### Design constraints

The manager is scoped by decision: `Current` is a plain field on it, one slot per DI scope, with no `AsyncLocal` anywhere. See [Why scoped, not ambient](#why-scoped-not-ambient). Dispose without `CompleteAsync` is an implicit rollback; `RollbackAsync` is how the owner of an observed-mode transaction reports its own rollback and suppresses the forgotten-completion warning. `OnCompleted` callbacks are process-local fast-path dispatchers, never the durability mechanism — durable delivery comes from rows committed in the transaction plus the consumer's recovery sweep. An `OnCompleted` fault after a successful commit leaves the unit `Completed` (the data is durable); a commit fault transitions to `Failed` before the exception propagates, so a retry cannot double-apply.

### Install

```bash
dotnet add package Headless.UnitOfWork.Abstractions
```

### Setup and use

```csharp
using Headless.UnitOfWork;  // IUnitOfWorkManager and the unit.Outbox accessor
// using Headless.Messaging; // only when you pass OutboxOptions

public sealed class PlaceOrderHandler(IUnitOfWorkManager unitOfWorkManager, AppDbContext db)
{
    public async Task<Result<OrderId>> Handle(PlaceOrder cmd, CancellationToken ct)
    {
        await using var unit = await unitOfWorkManager.BeginAsync(cancellationToken: ct); // or BeginAsync(db, ct) from the EF provider
        await unit.Outbox.PublishAsync(new OrderPlaced(orderId), ct);                     // row inside this unit's transaction
        await unit.CompleteAsync(ct);                                                     // commit, then dispatch
        return Result.Ok(orderId);
    }
}
```

`unit.Outbox` comes from `Headless.Messaging.Abstractions` (the implementation ships in `Headless.Messaging.Core` and is registered by `AddHeadlessMessaging`). Publishing the same message through an injected `IBus` instead would store a standalone row that outlives a rollback of this unit.

### Configuration

None.

### Runtime behavior

None.

---

## Headless.UnitOfWork

Implements the scoped `UnitOfWorkManager` (one unit-of-work slot per service scope, no `AsyncLocal`), the in-process unit engine with the atomic terminal claim, and `AddUnitOfWork()`.

### API and behavior

- `AddUnitOfWork()`: idempotent `TryAddScoped<IUnitOfWorkManager, UnitOfWorkManager>`; every consumer setup (`AddHeadlessMessaging`, `AddHeadlessJobs`, `AddHeadlessDbContextServices`, the three UnitOfWork provider setups) calls it, so exactly one registration exists regardless of which setup a host invokes first.
- Synchronous slot claim: the provider-facing `BeginAsync` claims the slot before its first `await`, so a concurrent begin in the same scope fails deterministically; a faulted resource begin releases the slot and propagates as-is.
- Join-by-default nesting (see [Nesting](#nesting-join-by-default)): same resource → child view; resource-less root + resource-bearing begin → independent nested unit; different resource → the catalogued throw. A root refuses `CompleteAsync` while a child is active and after a child was abandoned.
- `OnFailed` drain (log-and-continue) on rollback, abandon, scope dispose, commit fault, and child abandon; `RollbackAsync` idempotent; a commit fault transitions to `Failed` before the exception propagates.
- Leak detection: disposing the manager with an active unit rolls it back, runs `OnFailed` with `ScopeDisposed`, and logs the leak warning; an observed unit disposed un-completed after its transaction finished logs the forgotten-completion warning.
- `Adopt(unit)` (hidden): swap-and-restore, re-entrant when the slot already holds that unit; used internally by the EF save pipeline (see [The DbContext binding and adoption](#the-dbcontext-binding-and-adoption)).

### Design constraints

`CompleteAsync` in owned mode commits the resource, then drains `OnCompleted`, then disposes scope-local state; the terminal claim settles synchronously before the drain, so a racing dispose never rolls committed work back. A synchronous `Dispose` claims synchronously and offloads the rollback and failure drain to the thread pool (a captured `SynchronizationContext` can neither deadlock nor stall the disposing thread); `DisposeAsync` awaits the same path inline. Savepoint-blindness is harmless by construction: a row written inside a rolled-back savepoint vanishes and its stale `OnCompleted` callback no-ops — callbacks are process-local accelerators, and durability is the committed row plus the consumer's recovery sweep.

### Install

```bash
dotnet add package Headless.UnitOfWork
```

### Setup and use

```csharp
using Headless.UnitOfWork;

services.AddUnitOfWork();

await using var uow = await unitOfWorkManager.BeginAsync(cancellationToken: ct);
uow.OnCompleted(async () => await cache.RemoveAsync(key));
await uow.CompleteAsync(ct);
```

### Configuration

None.

### Runtime behavior

Registers the scoped `IUnitOfWorkManager`; the manager, engine, and handle types are internal. Repeated calls are idempotent. The manager is scoped — resolving it (or a scoped facade over it) from the root provider under scope validation throws, which is the correct captive-dependency signal.

---

## Headless.UnitOfWork.EntityFramework

Gives a plain EF Core `DbContext` the three unit-of-work entry points it needs: an owned begin, an observed enlist, and an execution-strategy-safe block.

### API and behavior

- `IUnitOfWorkManager.BeginAsync(db, isolation = ReadCommitted, ct)` — owned mode: claims the manager's slot synchronously, rejects a context that already has a transaction (naming `Enlist`), rejects a retrying execution strategy (naming `RunAsync`), begins the transaction eagerly, and records the `DbContext → IUnitOfWork` binding. `CompleteAsync` commits, then drains.
- `IUnitOfWorkManager.Enlist(db, transaction)` — observed mode for a transaction the caller commits: the unit's verbs are no-ops on the transaction; `CompleteAsync` drains without committing; `RollbackAsync` reports the caller's rollback and suppresses the forgotten-completion warning.
- `IUnitOfWorkManager.RunAsync(db, operation, isolation, ct)` (and the `TResult` overload) — begin → block → complete inside `db.Database.CreateExecutionStrategy()`. A retriable failure before commit replays with a fresh transaction and a fresh unit; once commit has started, or after `PreventRetry()`, the fault is captured and rethrown **outside** the strategy so EF cannot replay a possibly-committed block. A drain fault after a durable commit (an `OnCompleted` callback throwing once the unit is `Completed`) is logged and the block's result is returned — the same policy as the Npgsql/SqlClient `RunAsync` — because surfacing it would invite a retry that double-applies a committed block.
- `DbContextUnitOfWork.Find(db)` — public but hidden from IntelliSense; resolves the active unit bound to a context. The Headless save pipeline (in `Headless.EntityFramework`) consults it before the scope manager, so a factory-created context owning its own scope is still found.
- `AddEntityFrameworkUnitOfWork()` — idempotent; delegates to `AddUnitOfWork()` and registers nothing else.

### Design constraints

**Nesting is join-by-default.** A second `BeginAsync(db)` while a unit is active on the same context returns a child view over the same engine and the *same live resource* — no second transaction is begun. Child registrations transfer to the root on child complete; an abandoned child drops its registrations and aborts the root. A resource-bearing begin under a resource-less root opens an independent nested unit.

**The retrying-strategy split is deliberate.** `BeginAsync(db)` throws EF's own retrying-strategy message plus the `RunAsync` remedy because a user-initiated transaction cannot survive a strategy retry. `RunAsync` runs the begin *inside* the strategy, so retries replay the whole block with a fresh unit each attempt; the abandoned attempt's unit is unwound (rolled back) before the replay begins, or the replayed begin would meet a still-open transaction on the same context. The replay filter is: `CompleteAsync` started, or `IsRetryPrevented`, ⇒ rethrow outside the strategy. Reconcile an ambiguous post-commit fault with a client-generated key or another durable idempotency key before retrying the business operation.

**Observed mode is the advanced seam.** The save pipeline and the messaging inbox runners commit their own transactions and already know the outcome; they enlist, commit, then call `CompleteAsync` to drain.

**The binding uses a `ConditionalWeakTable`**, so a pooled context never leaks a stale unit: `Find` returns only a unit that is still `Active`; a terminal unit is ignored and evicted. This package never references `Headless.EntityFramework` (the reference flows the other way), so the provider stays usable by any EF consumer.

### Install

```bash
dotnet add package Headless.UnitOfWork.EntityFramework
```

### Setup and use

```csharp
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

services.AddDbContext<MyDbContext>(options => options.UseNpgsql(connectionString));
services.AddEntityFrameworkUnitOfWork();

// Owned mode: the transaction begins on this line; CompleteAsync commits and drains.
await using var unit = await unitOfWorkManager.BeginAsync(db, cancellationToken: ct);
db.Orders.Add(order);
await db.SaveChangesAsync(ct);
await unit.Outbox.PublishAsync(new OrderPlaced(order.Id), ct); // row in this transaction, dispatched after commit
await unit.CompleteAsync(ct);

// Retrying strategy configured? Run the unit as a retriable block instead:
await unitOfWorkManager.RunAsync(
    db,
    async (unit, ct) =>
    {
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await unit.Outbox.PublishAsync(new OrderPlaced(order.Id), ct);
    },
    cancellationToken: ct
);
```

The same two shapes apply to a `HeadlessDbContext` and a `HeadlessIdentityDbContext` (in `Headless.EntityFramework`) and to a plain `DbContext` alike: the receiver is always the scoped `IUnitOfWorkManager`, never the context.

An enlisted publish inside `RunAsync` keeps the block replayable: the unit is owned, so a retriable failure anywhere in the block replays the whole block with a fresh transaction and a fresh unit, the first attempt's row rolls back, and the replayed block publishes again. `unit.OnCompleted(async () => await bus.PublishAsync(…))` remains the alternative when a message is a post-commit notification that must not be atomic with the write; its message is lost if the process dies before the callback runs.

### Configuration

None.

### Runtime behavior

`AddEntityFrameworkUnitOfWork()` calls the idempotent `AddUnitOfWork()` (scoped `IUnitOfWorkManager`) and registers nothing else — no interceptor, no hosted service, no options. The manager is scoped: resolving it from the root provider under scope validation throws, which is the correct captive-dependency signal.

---

## Headless.UnitOfWork.PostgreSql

Runs raw-ADO `NpgsqlConnection` work as a scoped unit of work, so outbox rows and job rows written inside the transaction commit with it, dispatch after it commits, and are discarded when it rolls back.

### API and behavior

- `IUnitOfWorkManager.BeginAsync(connection, isolation, ct)` — owned mode: begins the transaction on that line (opening the connection when it is closed); `CompleteAsync` commits and drains, `RollbackAsync` or a dispose without completing rolls back.
- `IUnitOfWorkManager.Enlist(connection, transaction)` — observed mode for a transaction you commit yourself; call `CompleteAsync` after your commit or `RollbackAsync` after your rollback.
- `IUnitOfWorkManager.RunAsync(connection, operation, isolation, ct)` — begin → operation → complete in one call; a throwing operation rolls back and rethrows its own exception.
- `AddPostgreSqlUnitOfWork()` — registers the scoped manager (idempotent; there are no provider options).

### Design constraints

Npgsql exposes no commit edge, so observed mode is explicit: nothing completes the unit for you. A unit enlisted with `Enlist` and disposed without `CompleteAsync` or `RollbackAsync` after its transaction completed is logged as a forgotten completion — the durable rows are relay-recovered, but the fast-path dispatch was lost. A dispose while the transaction is still open is the normal failure path and logs nothing. There is no execution-strategy retry for raw ADO; that is an EF Core concept (`Headless.UnitOfWork.EntityFramework`).

### Install

```bash
dotnet add package Headless.UnitOfWork.PostgreSql
```

### Setup and use

```csharp
using Headless.UnitOfWork;
using Npgsql;

services.AddPostgreSqlUnitOfWork();

// unitOfWorkManager is the scoped IUnitOfWorkManager.
await using var unit = await unitOfWorkManager.BeginAsync(connection, cancellationToken: ct);
var relational = (IRelationalUnitOfWorkResource)unit.Resource!;
await using (var command = new NpgsqlCommand("INSERT INTO orders (id) VALUES (@id)", connection, (NpgsqlTransaction)relational.Transaction))
{
    command.Parameters.AddWithValue("id", orderId);
    await command.ExecuteNonQueryAsync(ct);
}
await unit.Outbox.PublishAsync(new OrderPlaced(orderId), ct); // enlisted; an injected IBus would not be
await unit.CompleteAsync(ct);
```

Observed mode, for a transaction you own:

```csharp
await using var tx = await connection.BeginTransactionAsync(ct);
await using var unit = unitOfWorkManager.Enlist(connection, tx);
// ... raw-ADO work + unit.Outbox publishes ...
await tx.CommitAsync(ct);
await unit.CompleteAsync(ct); // required — nothing completes the unit for you
```

### Configuration

None.

### Runtime behavior

Registers the scoped `IUnitOfWorkManager` only.

---

## Headless.UnitOfWork.SqlServer

Runs raw-ADO `SqlConnection` work as a scoped unit of work, so outbox rows and job rows written inside the transaction commit with it, dispatch after it commits, and are discarded when it rolls back.

### API and behavior

- `IUnitOfWorkManager.BeginAsync(connection, isolation, ct)` — owned mode: begins the transaction on that line (opening the connection when it is closed); `CompleteAsync` commits and drains, `RollbackAsync` or a dispose without completing rolls back.
- `IUnitOfWorkManager.Enlist(connection, transaction)` — observed mode for a transaction you commit yourself; call `CompleteAsync` after your commit or `RollbackAsync` after your rollback.
- `IUnitOfWorkManager.RunAsync(connection, operation, isolation, ct)` — begin → operation → complete in one call; a throwing operation rolls back and rethrows its own exception.
- `AddSqlServerUnitOfWork()` — registers the scoped manager (idempotent; there are no provider options).

### Design constraints

SqlClient exposes no commit edge, so observed mode is explicit: nothing completes the unit for you. A unit enlisted with `Enlist` and disposed without `CompleteAsync` or `RollbackAsync` after its transaction completed is logged as a forgotten completion — the durable rows are relay-recovered, but the fast-path dispatch was lost. A dispose while the transaction is still open is the normal failure path and logs nothing. There is no execution-strategy retry for raw ADO; that is an EF Core concept (`Headless.UnitOfWork.EntityFramework`).

### Install

```bash
dotnet add package Headless.UnitOfWork.SqlServer
```

### Setup and use

```csharp
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;

services.AddSqlServerUnitOfWork();

// unitOfWorkManager is the scoped IUnitOfWorkManager.
await using var unit = await unitOfWorkManager.BeginAsync(connection, cancellationToken: ct);
var relational = (IRelationalUnitOfWorkResource)unit.Resource!;
await using (var command = new SqlCommand("INSERT INTO orders (id) VALUES (@id)", connection, (SqlTransaction)relational.Transaction))
{
    command.Parameters.AddWithValue("@id", orderId);
    await command.ExecuteNonQueryAsync(ct);
}
await unit.Outbox.PublishAsync(new OrderPlaced(orderId), ct); // enlisted; an injected IBus would not be
await unit.CompleteAsync(ct);
```

Observed mode, for a transaction you own:

```csharp
await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
await using var unit = unitOfWorkManager.Enlist(connection, tx);
// ... raw-ADO work + unit.Outbox publishes ...
await tx.CommitAsync(ct);
await unit.CompleteAsync(ct); // required — nothing completes the unit for you
```

### Configuration

None.

### Runtime behavior

Registers the scoped `IUnitOfWorkManager` only.
