---
domain: Unit of Work
packages: UnitOfWork.Abstractions, UnitOfWork, UnitOfWork.EntityFramework, UnitOfWork.PostgreSql, UnitOfWork.SqlServer
---

# Unit of Work

> A scoped, explicitly-begun unit of work that lets Messaging and Jobs enlist durable writes in the caller's transaction and defer dispatch until it commits — with no `AsyncLocal`, no capture-before-await rule, and no service-provider argument to thread through.

## Orientation

An application developer opens a unit of work on the line they choose, does business work, publishes messages and enqueues jobs through the services they already inject, and completes it; everything inside is atomic with the transaction and dispatches after commit. The entry point is `IUnitOfWorkManager` (scoped), resolved like any other scoped service. Its `Current` property is a plain field — not an `AsyncLocal` — so there is no capture-before-first-await discipline to get wrong: whatever is resolved from the same DI scope sees the same `Current`, regardless of when during an `async` method the unit was begun.

`IUnitOfWorkManager.BeginAsync(...)` opens a resource-less coordination window; provider packages add resource-bearing overloads — `BeginAsync(db, ...)` for EF Core, `BeginAsync(connection, ...)` for raw ADO — that begin the transaction on that line (**owned mode**) and return an `IUnitOfWork` handle. `IUnitOfWork.CompleteAsync` commits the resource, then drains registered post-commit work in order; disposing without completing is an implicit rollback. `Enlist(resource, transaction)` is the advanced seam for code that already owns its commit edge (the Headless save pipeline, the messaging inbox runners) — **observed mode** — where the caller commits the transaction itself and `CompleteAsync` only drains.

Pick a provider by what owns the transaction:

- EF Core owns it (`DbContext`) → `Headless.UnitOfWork.EntityFramework`.
- Raw ADO on PostgreSQL → `Headless.UnitOfWork.PostgreSql`.
- Raw ADO on SQL Server → `Headless.UnitOfWork.SqlServer`.
- No relational resource at all (a script spanning several independent saves, a test harness) → the resource-less core member on `Headless.UnitOfWork` itself.

Messaging (`IBus`/`IQueue`) and Jobs (`ITimeJobManager<>`/`ICronJobManager<>`/`IJobScheduler`) are scoped facades over stateless singleton cores; they read `IUnitOfWorkManager.Current` at call time and enlist automatically when a compatible unit is active — no extra parameter, no explicit wiring. See [Choosing a Provider](#choosing-a-provider) for the package-selection table and [Guarantee Matrix](#guarantee-matrix) for exactly when a publish or a job write enlists versus writes autonomously versus throws.

## Agent Rules

- Resolve `IUnitOfWorkManager` (or a scoped facade over it — `IBus`, `IQueue`, a job manager) from a DI scope, never from the root provider. A scope models one operation; a singleton or hosted service that needs one of these creates its own scope (`IServiceScopeFactory.CreateScope()`). A host with `ValidateScopes` enabled turns a captive resolution into a startup-time error — the correct signal, not a bug to work around.
- Open the unit of work explicitly, on the line you choose. Nothing in this framework opens one on your behalf — no mediator behavior, no endpoint filter, no consumer-runtime wrapper. If a handler needs one, call `unitOfWork.BeginAsync(...)` (or `db.ExecuteTransactionAsync(...)` from `Headless.EntityFramework` for a `HeadlessDbContext`) yourself.
- There is no capture-before-first-await rule. Unlike the ambient design this replaced (see [Core Concepts § Why scoped, not ambient](#why-scoped-not-ambient)), `Current` is a plain field on a scoped object — begin it wherever is convenient in an `async` method; everything resolved from the same scope sees it.
- One resource per scope. Beginning again on the *same* resource while a unit is active joins it (returns a child handle); a *different* resource while a resource-bearing unit is active throws. Run unrelated transactional work in its own scope, not nested calls on the same manager.
- `Enlist(...)` is the advanced seam, not the default. Reach for `BeginAsync(...)` first — it begins the transaction and owns the commit. Use `Enlist` only when something else already owns the commit edge and you need the drain to piggyback on it (this is how the Headless save pipeline and the messaging inbox runners use it internally; most application code never calls it).
- `OnCompleted` callbacks are a fast path, never the durability mechanism. They are process-local, run once, receive no cancellation token, and are lost on a crash before they run. Durable delivery is the row committed in the transaction plus the consumer's own recovery sweep (the messaging relay, the jobs poller); a callback only dispatches that row sooner. Never make correctness depend on one running.
- Use `OnFailed` only to release a non-transactional resource reserved in anticipation of commit (a lock, a reservation) — not as a substitute for a proper rollback-safe design. Its faults are logged, never propagated.
- Do not resolve `IServiceProvider` and thread it through a "coordinated transaction" helper — that pattern is gone. Every entry point (`BeginAsync`, `Enlist`, `RunAsync`, and `HeadlessDbContextTransactionExtensions.ExecuteTransactionAsync`) self-sources the scope's manager; none of them take a `services:` parameter.
- Under a retrying EF execution strategy, `BeginAsync(db)` throws by design — a user-initiated transaction cannot survive a strategy replay. Use `unitOfWorkManager.RunAsync(db, ...)` (or, for a `HeadlessDbContext`, `db.ExecuteTransactionAsync(...)`), which runs begin → operation → complete *inside* the strategy and only lets a failure replay before the commit has started.
- Banned: reading `TransactionEnlistment` on a call and manually branching on whether a transaction is present. The framework's guarantee matrix (below) already encodes every combination and throws with a message naming the fix; hand-rolled branching duplicates and can drift from it.

## Core Concepts

### Scoped manager, not ambient

`IUnitOfWorkManager` is a scoped DI service. `Current` is a plain field it maintains — no `System.Threading.AsyncLocal<T>` exists anywhere in these packages. A unit of work is therefore visible to everything resolved from the same DI scope, from the moment it is begun, with no dependency on *when* during an `async` method the begin happened or which `await` boundaries separate the begin from a later read of `Current`.

#### Why scoped, not ambient

The design this replaced pushed a scope onto an `AsyncLocal` stack; because `AsyncLocal` mutations made inside an `async` callee do not flow back to the caller (`ExecutionContext` is copy-on-write and restored on return), the ambient design needed every provider to open its scope **synchronously, in the caller's own stack frame** — a discipline enforced only by comments and XML prose, and one that produced a real, hard-to-see bug: an `async` enlist helper set the ambient scope correctly inside itself, but the caller read `Current` back as `null`, silently turning a transactional outbox write into a non-atomic one while the happy-path test stayed green (`docs/solutions/logic-errors/asynclocal-ambient-scope-stranded-across-await.md`). A scoped manager has no such failure mode: `Current` is a field on an object every consumer in the scope already resolves through DI, so there is nothing to strand across an await. This mirrors precedent: MassTransit's `ScopedConsumeContextProvider` and Wolverine's `ScopedMessageContextHolder` (adopted specifically to fix the same class of ambient-propagation bug, GH-2583/GH-3001) both moved from `AsyncLocal` to a scoped field for the same reason.

The cost of this design is explicit: a singleton or a hosted service that needs `IUnitOfWorkManager` or a scoped facade over it (`IBus`, `IQueue`, a job manager) must create its own scope. Resolving one from the root container under `ValidateScopes` throws — by design, this is the framework surfacing a captive-dependency mistake rather than silently handing back a root-scoped instance.

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

`Current` always returns the innermost active frame: the child while it is active, the root again once the child completes.

### The `DbContext` binding and adoption

`Headless.UnitOfWork.EntityFramework`'s `BeginAsync(db)` and `Enlist(db, tx)` additionally record a binding from the `DbContext` instance to the unit (a `ConditionalWeakTable`, cleared once the unit reaches a terminal state). This exists because a context created through `IDbContextFactory<T>` owns its *own* DI scope — its manager is not the same object as the scope that resolved the factory. The Headless save pipeline (`Headless.EntityFramework`) consults this binding *before* its own scope's `IUnitOfWorkManager.Current`; when the bound unit belongs to a different scope's manager, the pipeline **adopts** it into its own scope for the save's duration (the hidden `IUnitOfWorkManager.Adopt(IUnitOfWork)`, swap-and-restore, re-entrant when the slot already holds that same unit), so domain-event handlers and the outbox dispatcher resolved in that scope see the same `Current`. Application code never calls `Adopt` directly — it exists so a factory-created context's transaction is still visible to everything the save touches.

### The failure hook and rollback

`IUnitOfWork.OnFailed(Func<UnitOfWorkFailure, ValueTask>)` runs after the unit reaches its failed terminal state — an explicit `RollbackAsync`, a dispose without completing (abandon), a commit fault, a child abandon aborting the root, or the owning scope disposing while the unit was still active. `UnitOfWorkFailure.Reason` is one of `RolledBack`, `Abandoned`, `Faulted`, `ScopeDisposed`, or `ChildAbandoned`; `Exception` carries the originating fault when one exists. Failure-hook faults are logged and never propagated — a cleanup callback must not be able to mask or replace the real failure.

`RollbackAsync()` is idempotent and legal in both modes: in owned mode it rolls the resource back; in observed mode it records that the caller's own transaction rolled back (and, like `CompleteAsync`, suppresses the forgotten-completion warning). A commit fault transitions the unit to `Failed` *before* the exception propagates, so a second `CompleteAsync` throws the "already failed" message rather than attempting to re-commit — this is the fix for a class of bug where a caller retries a commit whose outcome is actually unknown.

### `TransactionEnlistment` and the guarantee matrix

`TransactionEnlistment { WhenAvailable = 0 (default), Required = 1, Never = 2 }`, in `Headless.UnitOfWork.Abstractions`, is the one shared knob Messaging and Jobs both resolve with the same precedence: **per call > per type/function > host default**. It replaced two separate, differently-shaped flags — a Messaging delivery-mode value that forced enlistment and a Jobs boolean atomic-enlistment flag — with one enum and one guarantee matrix for both domains.

#### Guarantee Matrix

| `TransactionEnlistment` | Active unit of work, joinable compatible resource | No unit of work, or a unit with no joinable resource | Unit of work with an incompatible resource |
|---|---|---|---|
| `WhenAvailable` (default) | Row/write happens on the unit's transaction; dispatch/scheduler-restart/notification deferred to after commit | Autonomous durable write; the relay/poller dispatches it | Throws |
| `Required` | Same as above | Throws | Throws |
| `Never` | Autonomous — never enlists, even though a resource is active | Autonomous | Autonomous — never inspects the resource |

"No joinable resource" behaves exactly like "no unit of work" — a resource-less `BeginAsync()` gives Messaging's in-memory storage something to join through its buffered-promotion seam, but a relational storage or a Jobs write needs a *relational* resource on the *same database* to enlist into. An incompatible resource (a different database, a closed or completed transaction) throws for every value except `Never`, regardless of `WhenAvailable` or `Required` — the framework never silently downgrades a requested enlistment.

For Messaging, `DeliveryMode.Direct` bypasses storage and coordination entirely and implies `Never` (it still rejects `Delay`/`ScheduledAt`); see `messaging.md#delivery-modes`. For Jobs, composition across host/function/call tiers is strictest-wins (`Required` > `WhenAvailable` > `Never`); see `jobs.md`.

### Message catalogue

Every illegal transition throws with a message naming the remedy — never a bare `InvalidOperationException`:

| Condition | Message |
|---|---|
| `BeginAsync(db)` with an existing transaction on the context | `The DbContext already has an active transaction. Begin the unit of work before beginning the transaction, or call IUnitOfWorkManager.Enlist(db, transaction) for a transaction you commit yourself.` |
| `BeginAsync(db)` under a retrying execution strategy | EF's own text, plus: `Use IUnitOfWorkManager.RunAsync(db, …) to run the unit of work as a retriable block.` |
| `CompleteAsync` after `Completed` | `The unit of work has already completed. Begin a new unit of work for further work.` |
| `CompleteAsync` after `Failed` | `The unit of work has already failed ({Reason}) and cannot be completed. Begin a new unit of work.` |
| `OnCompleted`/`OnFailed`/`GetOrAdd` after a terminal state | `The unit of work is {State}; registrations are accepted only while it is Active.` |
| A member call after dispose | `ObjectDisposedException("UnitOfWork")` |
| A second `BeginAsync`/`Enlist` on a *different* resource | `A unit of work is already active on another resource in this scope. Complete it first, or run the second operation in its own service scope (IServiceScopeFactory.CreateScope()).` |
| `BeginAsync`/`Enlist` while another begin is in flight in the same scope | `Another unit of work is being begun concurrently in this scope. Await the first BeginAsync before beginning again, or run parallel work in separate service scopes.` |
| Root `CompleteAsync` while a child is still active | `A nested unit of work begun in this scope is still active. Complete or dispose it before completing the root.` |
| Root `CompleteAsync` after a child was abandoned | `A nested unit of work was disposed without completing, so the root cannot complete; the transaction is rolled back.` |
| A caller-owned transaction saved with integration events but no unit bound to the context (or a resource-less bound unit) | `SaveChanges ran inside a caller-owned transaction that no unit of work owns, so integration events and jobs would dispatch non-atomically. Begin the unit of work on this context (IUnitOfWorkManager.BeginAsync(db)) before beginning the transaction, or enlist the transaction with Enlist(db, transaction).` |
| Publishing with `TransactionEnlistment.Required` and no active unit of work | `Publishing requires an active unit of work (TransactionEnlistment.Required) but none is active in this scope. Begin one with IUnitOfWorkManager.BeginAsync before publishing, or register the message with TransactionEnlistment.WhenAvailable.` |
| Scheduling a job with `TransactionEnlistment.Required` and no active unit of work | `Scheduling '{Function}' requires an active unit of work (TransactionEnlistment.Required) but none is active in this scope. Begin one with IUnitOfWorkManager.BeginAsync, or register the function with TransactionEnlistment.WhenAvailable.` |
| An incompatible resource (Messaging) | `The active unit of work's transaction belongs to another database ({Mismatch}), so publishing cannot enlist. Use the same database, or TransactionEnlistment.Never for this call.` |
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
| The resource-less core (`Headless.UnitOfWork`, no provider) | A script spanning several independent transactional saves that should still share one commit-drain edge; a test harness. | Any case that needs a joinable relational resource — a resource-less unit only lets a *relational* provider join by opening an *independent nested unit*, not by sharing the same transaction. | Nothing enlists directly on it; each resource-bearing operation underneath opens and commits its own transaction. |

---

## Headless.UnitOfWork.Abstractions

Defines the public unit-of-work contracts without provider dependencies: the scoped manager entry point, the unit handle, the resource seams, and the shared `TransactionEnlistment` knob.

### Problem Solved

Consumer packages (`Headless.Messaging.Abstractions`, `Headless.Jobs.Abstractions`) need `IUnitOfWork`, `IUnitOfWorkManager`, and `TransactionEnlistment` without pulling in a concrete manager implementation or any provider. This package is that zero-dependency contract surface.

### Key Features

- `IUnitOfWorkManager` (scoped): `Current`, the resource-less `BeginAsync(options?, ct)`, plus the provider primitives — the resource-factory `BeginAsync` (hidden from IntelliSense), observed-mode `Enlist` (hidden), and swap-and-restore `Adopt` (hidden).
- `IUnitOfWork`: `State`, `Failure`, `Resource`, `OnCompleted(Func<ValueTask>)`, `OnFailed(Func<UnitOfWorkFailure, ValueTask>)`, `GetOrAdd<TState>` (both overloads), `PreventRetry()` / `IsRetryPrevented`, `CompleteAsync(ct)`, idempotent `RollbackAsync()`, dispose both ways.
- `IUnitOfWorkResource` (`IsOwned`, `IsTransactionCompleted`, `CommitAsync`, `RollbackAsync`) and `IRelationalUnitOfWorkResource` (`Connection`, `Transaction`, non-null while active).
- `UnitOfWorkState` (`Active = 0`, `Completed = 1`, `Failed = 2`); `UnitOfWorkFailure` with `UnitOfWorkFailureReason` (`Unspecified`, `RolledBack`, `Abandoned`, `Faulted`, `ScopeDisposed`, `ChildAbandoned`).
- `UnitOfWorkOptions`: intentionally empty today; propagation knobs (`RequiresNew`/`Suppress`) land here additively.
- `TransactionEnlistment { WhenAvailable = 0, Required = 1, Never = 2 }`: see [Guarantee Matrix](#guarantee-matrix).

### Design Notes

The manager is scoped by decision: `Current` is a plain field on it, one slot per DI scope, with no `AsyncLocal` anywhere. See [Why scoped, not ambient](#why-scoped-not-ambient). Dispose without `CompleteAsync` is an implicit rollback; `RollbackAsync` is how the owner of an observed-mode transaction reports its own rollback and suppresses the forgotten-completion warning. `OnCompleted` callbacks are process-local fast-path dispatchers, never the durability mechanism — durable delivery comes from rows committed in the transaction plus the consumer's recovery sweep. An `OnCompleted` fault after a successful commit leaves the unit `Completed` (the data is durable); a commit fault transitions to `Failed` before the exception propagates, so a retry cannot double-apply.

### Installation

```bash
dotnet add package Headless.UnitOfWork.Abstractions
```

### Quick Start

```csharp
using Headless.UnitOfWork;

public sealed class PlaceOrderHandler(IUnitOfWorkManager unitOfWork, AppDbContext db, IBus bus)
{
    public async Task<Result<OrderId>> Handle(PlaceOrder cmd, CancellationToken ct)
    {
        await using var uow = await unitOfWork.BeginAsync(cancellationToken: ct); // or BeginAsync(db, ct) from the EF provider
        await bus.PublishAsync(new OrderPlaced(orderId), ct);                    // joins the active unit
        await uow.CompleteAsync(ct);                                             // commit, then dispatch
        return Result.Ok(orderId);
    }
}
```

### Configuration

None.

### Dependencies

None.

### Side Effects

None.

---

## Headless.UnitOfWork

Implements the scoped `UnitOfWorkManager` (one unit-of-work slot per service scope, no `AsyncLocal`), the in-process unit engine with the atomic terminal claim, and `AddUnitOfWork()`.

### Problem Solved

The default, provider-agnostic implementation behind `IUnitOfWorkManager`: nesting, the failure/completion drain, leak detection, and the concurrent-begin latch, none of which any provider package needs to reimplement.

### Key Features

- `AddUnitOfWork()`: idempotent `TryAddScoped<IUnitOfWorkManager, UnitOfWorkManager>`; every consumer setup (`AddHeadlessMessaging`, `AddHeadlessJobs`, `AddHeadlessDbContextServices`, the three UnitOfWork provider setups) calls it, so exactly one registration exists regardless of which setup a host invokes first.
- Synchronous slot claim: the provider-facing `BeginAsync` claims the slot before its first `await`, so a concurrent begin in the same scope fails deterministically; a faulted resource begin releases the slot and propagates as-is.
- Join-by-default nesting (see [Nesting](#nesting-join-by-default)): same resource → child view; resource-less root + resource-bearing begin → independent nested unit; different resource → the catalogued throw. A root refuses `CompleteAsync` while a child is active and after a child was abandoned.
- `OnFailed` drain (log-and-continue) on rollback, abandon, scope dispose, commit fault, and child abandon; `RollbackAsync` idempotent; a commit fault transitions to `Failed` before the exception propagates.
- Leak detection: disposing the manager with an active unit rolls it back, runs `OnFailed` with `ScopeDisposed`, and logs the leak warning; an observed unit disposed un-completed after its transaction finished logs the forgotten-completion warning.
- `Adopt(unit)` (hidden): swap-and-restore, re-entrant when the slot already holds that unit; used internally by the EF save pipeline (see [The DbContext binding and adoption](#the-dbcontext-binding-and-adoption)).

### Design Notes

`CompleteAsync` in owned mode commits the resource, then drains `OnCompleted`, then disposes scope-local state; the terminal claim settles synchronously before the drain, so a racing dispose never rolls committed work back. A synchronous `Dispose` claims synchronously and offloads the rollback and failure drain to the thread pool (a captured `SynchronizationContext` can neither deadlock nor stall the disposing thread); `DisposeAsync` awaits the same path inline. Savepoint-blindness is harmless by construction: a row written inside a rolled-back savepoint vanishes and its stale `OnCompleted` callback no-ops — callbacks are process-local accelerators, and durability is the committed row plus the consumer's recovery sweep.

### Installation

```bash
dotnet add package Headless.UnitOfWork
```

### Quick Start

```csharp
using Headless.UnitOfWork;

services.AddUnitOfWork();

await using var uow = await unitOfWorkManager.BeginAsync(cancellationToken: ct);
uow.OnCompleted(async () => await cache.RemoveAsync(key));
await uow.CompleteAsync(ct);
```

### Configuration

None.

### Dependencies

- `Headless.Checks`
- `Headless.UnitOfWork.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

Registers the scoped `IUnitOfWorkManager`; the manager, engine, and handle types are internal. Repeated calls are idempotent. The manager is scoped — resolving it (or a scoped facade over it) from the root provider under scope validation throws, which is the correct captive-dependency signal.

---

## Headless.UnitOfWork.EntityFramework

Gives a plain EF Core `DbContext` the three unit-of-work entry points it needs: an owned begin, an observed enlist, and an execution-strategy-safe block.

### Problem Solved

Application code and framework infrastructure alike (the Headless save pipeline, Jobs' EF operational store) need to begin, enlist, or run a retriable unit of work against a `DbContext` without hand-rolling transaction-begin plus manual enlistment plus a replay-safety filter every time.

### Key Features

- `IUnitOfWorkManager.BeginAsync(db, isolation = ReadCommitted, ct)` — owned mode: claims the manager's slot synchronously, rejects a context that already has a transaction (naming `Enlist`), rejects a retrying execution strategy (naming `RunAsync`), begins the transaction eagerly, and records the `DbContext → IUnitOfWork` binding. `CompleteAsync` commits, then drains.
- `IUnitOfWorkManager.Enlist(db, transaction)` — observed mode for a transaction the caller commits: the unit's verbs are no-ops on the transaction; `CompleteAsync` drains without committing; `RollbackAsync` reports the caller's rollback and suppresses the forgotten-completion warning.
- `IUnitOfWorkManager.RunAsync(db, operation, isolation, ct)` (and the `TResult` overload) — begin → block → complete inside `db.Database.CreateExecutionStrategy()`. A retriable failure before commit replays with a fresh transaction and a fresh unit; once commit has started, or after `PreventRetry()`, the fault is captured and rethrown **outside** the strategy so EF cannot replay a possibly-committed block.
- `DbContextUnitOfWork.Find(db)` — public but hidden from IntelliSense; resolves the active unit bound to a context. The Headless save pipeline (in `Headless.EntityFramework`) consults it before the scope manager, so a factory-created context owning its own scope is still found.
- `AddEntityFrameworkUnitOfWork()` — idempotent; delegates to `AddUnitOfWork()` and registers nothing else.

### Design Notes

**Nesting is join-by-default.** A second `BeginAsync(db)` while a unit is active on the same context returns a child view over the same engine and the *same live resource* — no second transaction is begun. Child registrations transfer to the root on child complete; an abandoned child drops its registrations and aborts the root. A resource-bearing begin under a resource-less root opens an independent nested unit.

**The retrying-strategy split is deliberate.** `BeginAsync(db)` throws EF's own retrying-strategy message plus the `RunAsync` remedy because a user-initiated transaction cannot survive a strategy retry. `RunAsync` runs the begin *inside* the strategy, so retries replay the whole block with a fresh unit each attempt; the abandoned attempt's unit is unwound (rolled back) before the replay begins, or the replayed begin would meet a still-open transaction on the same context. The replay filter is: `CompleteAsync` started, or `IsRetryPrevented`, ⇒ rethrow outside the strategy. Reconcile an ambiguous post-commit fault with a client-generated key or another durable idempotency key before retrying the business operation.

**Observed mode is the advanced seam.** The save pipeline and the messaging inbox runners commit their own transactions and already know the outcome; they enlist, commit, then call `CompleteAsync` to drain.

**The binding uses a `ConditionalWeakTable`**, so a pooled context never leaks a stale unit: `Find` returns only a unit that is still `Active`; a terminal unit is ignored and evicted. This package never references `Headless.EntityFramework` (the reference flows the other way), so the provider stays usable by any EF consumer.

### Installation

```bash
dotnet add package Headless.UnitOfWork.EntityFramework
```

### Quick Start

```csharp
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

services.AddDbContext<MyDbContext>(options => options.UseNpgsql(connectionString));
services.AddEntityFrameworkUnitOfWork();

// Owned mode: the transaction begins on this line; CompleteAsync commits and drains.
await using var unitOfWork = await unitOfWorkManager.BeginAsync(db, cancellationToken: ct);
db.Orders.Add(order);
await db.SaveChangesAsync(ct);
unitOfWork.OnCompleted(async () => await bus.PublishAsync(new OrderPlaced(order.Id), ct));
await unitOfWork.CompleteAsync(ct);

// Retrying strategy configured? Run the unit as a retriable block instead:
await unitOfWorkManager.RunAsync(
    db,
    async (unitOfWork, ct) =>
    {
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        unitOfWork.OnCompleted(async () => await bus.PublishAsync(new OrderPlaced(order.Id), ct));
    },
    cancellationToken: ct
);
```

For any `IHeadlessDbContext` (a `HeadlessDbContext` or `HeadlessIdentityDbContext`, in `Headless.EntityFramework`), the same shape is available without resolving the manager yourself:

```csharp
using Microsoft.EntityFrameworkCore;

await db.ExecuteTransactionAsync(async (context, ct) =>
{
    context.Orders.Add(order);
    await context.SaveChangesAsync(ct);
    await bus.PublishAsync(new OrderPlaced(order.Id), ct);
}, cancellationToken: ct);
```

### Configuration

None.

### Dependencies

- `Headless.Checks`
- `Headless.UnitOfWork`
- `Microsoft.EntityFrameworkCore.Relational`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

### Side Effects

`AddEntityFrameworkUnitOfWork()` calls the idempotent `AddUnitOfWork()` (scoped `IUnitOfWorkManager`) and registers nothing else — no interceptor, no hosted service, no options. The manager is scoped: resolving it from the root provider under scope validation throws, which is the correct captive-dependency signal.

---

## Headless.UnitOfWork.PostgreSql

Runs raw-ADO `NpgsqlConnection` work as a scoped unit of work, so outbox rows and job rows written inside the transaction commit with it, dispatch after it commits, and are discarded when it rolls back.

### Problem Solved

Raw ADO on PostgreSQL has no framework transaction abstraction and no commit interceptor to observe; this package gives it the same `BeginAsync`/`Enlist`/`RunAsync` shape as the EF provider, with the completion made fully explicit where PostgreSQL gives no commit edge to observe.

### Key Features

- `IUnitOfWorkManager.BeginAsync(connection, isolation, ct)` — owned mode: begins the transaction on that line (opening the connection when it is closed); `CompleteAsync` commits and drains, `RollbackAsync` or a dispose without completing rolls back.
- `IUnitOfWorkManager.Enlist(connection, transaction)` — observed mode for a transaction you commit yourself; call `CompleteAsync` after your commit or `RollbackAsync` after your rollback.
- `IUnitOfWorkManager.RunAsync(connection, operation, isolation, ct)` — begin → operation → complete in one call; a throwing operation rolls back and rethrows its own exception.
- `AddPostgreSqlUnitOfWork()` — registers the scoped manager (idempotent; there are no provider options).

### Design Notes

Npgsql exposes no commit edge, so observed mode is explicit: nothing completes the unit for you. A unit enlisted with `Enlist` and disposed without `CompleteAsync` or `RollbackAsync` after its transaction completed is logged as a forgotten completion — the durable rows are relay-recovered, but the fast-path dispatch was lost. A dispose while the transaction is still open is the normal failure path and logs nothing. There is no execution-strategy retry for raw ADO; that is an EF Core concept (`Headless.UnitOfWork.EntityFramework`).

### Installation

```bash
dotnet add package Headless.UnitOfWork.PostgreSql
```

### Quick Start

```csharp
using Headless.UnitOfWork;
using Npgsql;

services.AddPostgreSqlUnitOfWork();

// unitOfWork is the scoped IUnitOfWorkManager; bus is the scoped IBus facade.
await using var unit = await unitOfWork.BeginAsync(connection, cancellationToken: ct);
var relational = (IRelationalUnitOfWorkResource)unit.Resource!;
await using (var command = new NpgsqlCommand("INSERT INTO orders (id) VALUES (@id)", connection, (NpgsqlTransaction)relational.Transaction))
{
    command.Parameters.AddWithValue("id", orderId);
    await command.ExecuteNonQueryAsync(ct);
}
await bus.PublishAsync(new OrderPlaced(orderId), ct);
await unit.CompleteAsync(ct);
```

Observed mode, for a transaction you own:

```csharp
await using var tx = await connection.BeginTransactionAsync(ct);
await using var unit = unitOfWork.Enlist(connection, tx);
// ... raw-ADO work + publishes ...
await tx.CommitAsync(ct);
await unit.CompleteAsync(ct); // required — nothing completes the unit for you
```

### Configuration

None.

### Dependencies

- `Headless.Checks`
- `Headless.UnitOfWork`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `Npgsql`

### Side Effects

Registers the scoped `IUnitOfWorkManager` only.

---

## Headless.UnitOfWork.SqlServer

Runs raw-ADO `SqlConnection` work as a scoped unit of work, so outbox rows and job rows written inside the transaction commit with it, dispatch after it commits, and are discarded when it rolls back.

### Problem Solved

Same as `Headless.UnitOfWork.PostgreSql`, for SQL Server: SqlClient has no commit interceptor to observe, so this package gives raw ADO the same `BeginAsync`/`Enlist`/`RunAsync` shape with fully explicit completion.

### Key Features

- `IUnitOfWorkManager.BeginAsync(connection, isolation, ct)` — owned mode: begins the transaction on that line (opening the connection when it is closed); `CompleteAsync` commits and drains, `RollbackAsync` or a dispose without completing rolls back.
- `IUnitOfWorkManager.Enlist(connection, transaction)` — observed mode for a transaction you commit yourself; call `CompleteAsync` after your commit or `RollbackAsync` after your rollback.
- `IUnitOfWorkManager.RunAsync(connection, operation, isolation, ct)` — begin → operation → complete in one call; a throwing operation rolls back and rethrows its own exception.
- `AddSqlServerUnitOfWork()` — registers the scoped manager (idempotent; there are no provider options).

### Design Notes

SqlClient exposes no commit edge, so observed mode is explicit: nothing completes the unit for you. A unit enlisted with `Enlist` and disposed without `CompleteAsync` or `RollbackAsync` after its transaction completed is logged as a forgotten completion — the durable rows are relay-recovered, but the fast-path dispatch was lost. A dispose while the transaction is still open is the normal failure path and logs nothing. There is no execution-strategy retry for raw ADO; that is an EF Core concept (`Headless.UnitOfWork.EntityFramework`).

### Installation

```bash
dotnet add package Headless.UnitOfWork.SqlServer
```

### Quick Start

```csharp
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;

services.AddSqlServerUnitOfWork();

// unitOfWork is the scoped IUnitOfWorkManager; bus is the scoped IBus facade.
await using var unit = await unitOfWork.BeginAsync(connection, cancellationToken: ct);
var relational = (IRelationalUnitOfWorkResource)unit.Resource!;
await using (var command = new SqlCommand("INSERT INTO orders (id) VALUES (@id)", connection, (SqlTransaction)relational.Transaction))
{
    command.Parameters.AddWithValue("@id", orderId);
    await command.ExecuteNonQueryAsync(ct);
}
await bus.PublishAsync(new OrderPlaced(orderId), ct);
await unit.CompleteAsync(ct);
```

Observed mode, for a transaction you own:

```csharp
await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
await using var unit = unitOfWork.Enlist(connection, tx);
// ... raw-ADO work + publishes ...
await tx.CommitAsync(ct);
await unit.CompleteAsync(ct); // required — nothing completes the unit for you
```

### Configuration

None.

### Dependencies

- `Headless.Checks`
- `Headless.UnitOfWork`
- `Microsoft.Data.SqlClient`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

### Side Effects

Registers the scoped `IUnitOfWorkManager` only.
