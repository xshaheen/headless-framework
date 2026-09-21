---
domain: Unit of Work
packages: UnitOfWork.Abstractions, UnitOfWork, UnitOfWork.EntityFramework, UnitOfWork.PostgreSql, UnitOfWork.SqlServer
---

# Unit of Work

> An explicitly-begun unit of work, opened from a singleton factory and reached by the code that needs it through the handle or the object it was begun on, that lets Messaging and Jobs enlist durable writes in the caller's transaction and defer dispatch until it commits — with no `AsyncLocal`, no scoped slot, no capture-before-await rule, and nothing ambient to discover.

## Orientation

An application developer opens a unit of work on the line they choose, does business work, publishes messages and schedules jobs **through the unit**, and completes it; everything enlisted inside is atomic with the transaction and dispatches after commit. The entry point is `IUnitOfWorkFactory`, a **singleton**: it holds no per-scope state, so a controller, a consumer, a hosted service, and a background job all inject the same object and call `BeginAsync(...)` on it. There is no `Current`. The unit a caller opened is the `IUnitOfWork` handle it holds, and code that needs that unit reaches it one of three ways: it is handed the handle; it reads it from the object the unit was begun on (`db.UnitOfWork()`, `connection.UnitOfWork()`, `ConsumeContext.UnitOfWork`); or it wraps its own work in `RunAsync(db, …)` / `RunAsync(connection, …)` on that same object, which **joins** the live unit instead of opening a second one. That last path is transaction propagation without an ambient: a service that self-wraps in `RunAsync` composes under any caller that already opened the transaction on the same context or connection.

`IUnitOfWorkFactory.BeginAsync(...)` opens a resource-less coordination window; provider packages add resource-bearing overloads — `BeginAsync(db, ...)` for EF Core, `BeginAsync(connection, ...)` for raw ADO — that begin the transaction on that line (**owned mode**) and return the handle. `IUnitOfWork.CompleteAsync` commits the resource, then drains registered post-commit work in order; disposing without completing is an implicit rollback. `Enlist(resource, transaction)` is the advanced seam for code that already owns its commit edge (the Headless save pipeline, the messaging inbox runners) — **observed mode** — where the caller commits the transaction itself and `CompleteAsync` only drains.

Pick a provider by what owns the transaction:

- EF Core owns it (`DbContext`) → `Headless.UnitOfWork.EntityFramework`.
- Raw ADO on PostgreSQL → `Headless.UnitOfWork.PostgreSql`.
- Raw ADO on SQL Server → `Headless.UnitOfWork.SqlServer`.
- No relational resource at all (a script spanning several independent saves, a test harness) → the resource-less core member on `Headless.UnitOfWork` itself.

Both participant domains enlist through the unit, and the difference between an enlisted and an autonomous write is the receiver at the call site:

- **Messaging:** `IBus` and `IQueue` are autonomous singletons; a publish through them writes a standalone durable row that survives the caller's rollback. To write a message inside the transaction, publish through the unit — `unit.Outbox.PublishAsync(...)` / `unit.Outbox.EnqueueAsync(...)`, an accessor `Headless.Messaging.Abstractions` adds to `IUnitOfWork`. That surface always enlists and refuses, before any effect, when the configured storage cannot join the unit. See [messaging.md § Delivery Modes](messaging.md#delivery-modes).
- **Jobs:** the injected `IJobScheduler`, `ITimeJobManager<>`, and `ICronJobManager<>` are autonomous singletons; a schedule through them writes outside any transaction and the poller picks it up. To schedule inside the transaction, go through the unit — `unit.Jobs`, `unit.TimeJobs<T>()`, `unit.CronJobs<T>()`, accessors `Headless.Jobs.Abstractions` adds to `IUnitOfWork`. Those always enlist and refuse when the unit carries no live relational resource for the job store. `TransactionEnlistment.Required` on a function or a call is the guard against the wrong receiver: it makes the autonomous one throw. See [Guarantee Matrix](#guarantee-matrix).

See [Choosing a Provider](#choosing-a-provider) for the package-selection table.

## Agent Rules

- Inject `IUnitOfWorkFactory` anywhere — it is a singleton with no scope-bound state, so a hosted service or a framework singleton depends on it directly and needs no `IServiceScopeFactory` dance. The same is true of `IBus`, `IQueue`, `IJobScheduler`, and the Jobs managers: all singletons, all autonomous.
- To enlist a write, call it on the handle you hold: `unit.Outbox.PublishAsync(...)`, `unit.Jobs.ScheduleAsync(...)`, `unit.TimeJobs<T>().AddAsync(...)`. The injected publishers and schedulers never enlist, whatever scope they came from. The accessors are free to read at each call site — each binds once per unit and is kept as unit-local state — and refuse a unit that already reached a terminal state; a binding retained past that point throws on its next use, before anything is stored.
- Reach the unit through the handle or the object it was begun on. Nothing ambient carries it, so a callee that must enlist takes the `IUnitOfWork` as a parameter, reads it from the `DbContext` or `DbConnection` it was begun on (`db.UnitOfWork()`, `connection.UnitOfWork()`), reads `context.UnitOfWork` in a transactional consumer, or wraps its own work in `RunAsync(db, …)` / `RunAsync(connection, …)` on that object — which joins the live unit. Do not `BeginAsync` a second unit inside a callee to "get one": on a context or connection that already carries a live unit, `BeginAsync` and `Enlist` throw and name the join.
- Open the unit of work explicitly, on the line you choose. Nothing in this framework opens one on your behalf — no mediator behavior, no endpoint filter, no consumer-runtime wrapper. If a handler needs one, call `factory.BeginAsync(...)` or `factory.RunAsync(...)` yourself.
- Two begins are two units. The factory has no slot: consecutive or concurrent `BeginAsync` calls on *different* resources each open an independent unit with its own resource, drain, and outcome, and neither knows about the other. The join is keyed on the resource, never on the factory or a scope: `RunAsync` on a bound `DbContext`/`DbConnection` joins, `BeginAsync` on one is refused, and a begin on any other object is simply a new unit. There are no child views — a joined block receives the owner's own handle.
- `Enlist(...)` is the advanced seam, not the default. Reach for `BeginAsync(...)` first — it begins the transaction and owns the commit. Use `Enlist` only when something else already owns the commit edge and you need the drain to piggyback on it (this is how the Headless save pipeline and the messaging inbox runners use it internally; most application code never calls it).
- `OnCompleted` callbacks are a fast path, never the durability mechanism. They are process-local, run once, receive no cancellation token, and are lost on a crash before they run. Durable delivery is the row committed in the transaction plus the consumer's own recovery sweep (the messaging relay, the jobs poller); a callback only dispatches that row sooner. Never make correctness depend on one running.
- Use `OnFailed` only to release a non-transactional resource reserved in anticipation of commit (a lock, a reservation) — not as a substitute for a proper rollback-safe design. Its faults are logged, never propagated.
- The factory is the only receiver that opens a unit of work. `BeginAsync`, `Enlist`, and `RunAsync` are extension members on `IUnitOfWorkFactory`; no context, connection, or helper type carries a second spelling, and none of them take a `services:` parameter.
- Under a retrying EF execution strategy, `BeginAsync(db)` throws by design — a user-initiated transaction cannot survive a strategy replay. Use `factory.RunAsync(db, ...)`, which runs begin → operation → complete *inside* the strategy and only lets a failure replay before the commit has started.
- Replay re-runs the block that owns the unit, so an enlisted publish or Jobs write issued directly inside your own `RunAsync(db, …)` block leaves it replayable: a transient failure anywhere in the block replays it with a fresh unit, the first attempt's rows roll back, and the replayed block writes them again. An enlisted write ends replay where a replay would not re-run it. One place is an *observed-mode* unit — the `HeadlessDbContext` save pipeline's own save, from a domain-event handler, which the pipeline replays without re-running the handler; there both the publish and the Jobs write call `IUnitOfWork.PreventRetry()` before writing. The other is a `SaveChangesAsync` inside your own block that dispatched domain or integration events: the successful save clears the aggregate's events, so a replayed block would re-insert the aggregate with nothing left to dispatch and commit it without the handlers' rows; the save calls `PreventRetry()` before clearing them. After either mark the fault is surfaced outside the strategy, so reconcile an ambiguous post-commit fault with a durable idempotency key instead of retrying blind. The EF integration-event dispatcher is exempt in the pipeline-owned save: it marks the occurrences the save pipeline re-publishes on a replayed attempt.
- Banned: reading Jobs' `TransactionEnlistment` on a call and manually branching on whether a transaction is present. The framework's guarantee matrix (below) already encodes every combination and throws with a message naming the fix; hand-rolled branching duplicates and can drift from it.
- `GetFeature<TFeature>()` is a bridge-package seam, not an application API. Use the typed accessors a bridge ships (`unit.Outbox`, `unit.Jobs`); register a feature (a singleton implementing `IUnitOfWorkFeature`) only when writing such a bridge.

## Core Concepts

### A singleton factory, a handle you hold

`IUnitOfWorkFactory` is a singleton DI service that creates units; it keeps no record of them. `BeginAsync` returns an `IUnitOfWork` handle, and that handle is the unit's only identity: whoever holds it can register post-commit work on it, enlist writes through it, and complete or roll it back. No `System.Threading.AsyncLocal<T>` and no per-scope slot exist anywhere in these packages, so there is nothing to strand across an `await`, nothing to leak between requests, and no captive-dependency trap for a singleton to fall into.

#### Why a handle, not an ambient or scoped "current"

Two designs preceded this one. The first pushed a scope onto an `AsyncLocal` stack; because `AsyncLocal` mutations made inside an `async` callee do not flow back to the caller, an `async` enlist helper set the ambient scope correctly inside itself while the caller read it back as `null`, silently turning a transactional outbox write into a non-atomic one while the happy-path test stayed green (`docs/solutions/logic-errors/asynclocal-ambient-scope-stranded-across-await.md`). The second kept a `Current` field on a scoped manager, which fixed that bug but brought its own costs: every enlisting receiver had to be scoped too (so a singleton could never hold one), a factory-created `DbContext` owning its own scope needed an adoption dance to make its unit visible, and "begin while one is active" needed join-by-default nesting with child views, transfer-on-complete, and abandon-aborts-root rules — a lot of machinery whose only job was to guess which unit a caller meant.

The handle design removes the guess. The unit a caller means is the one it holds, or the one bound to the resource it holds — and that binding is explicit, keyed on an object both layers demonstrably share, not on a scope or an execution context. Propagation survives as `RunAsync` joining the resource's live unit; what is gone is the machinery that inferred a unit from where the code happened to be running.

### Owned vs observed mode

Owned vs observed is a flag on the enlisted `IUnitOfWorkResource`, never a second handle type — `IUnitOfWork` looks the same either way.

- **Owned mode** (`BeginAsync(...)`): the unit begins the transaction itself, on that line, and owns the commit. `CompleteAsync` commits the resource, then drains `OnCompleted` registrations, then disposes unit-local state. A dispose without `CompleteAsync` rolls the transaction back.
- **Observed mode** (`Enlist(resource, transaction)`): the caller already owns and commits the transaction. The resource's `CommitAsync`/`RollbackAsync` are no-ops; `CompleteAsync` only drains. The caller must call `CompleteAsync` after its own commit, or `RollbackAsync` after its own rollback — a dispose that follows neither, after the transaction has already finished, is logged as a forgotten completion (the durable rows are still relay-recovered; only the fast-path drain was lost).

There is one commit verb regardless of mode: `IUnitOfWork.CompleteAsync`. There is no third, interceptor-driven mode — the Headless save pipeline and the messaging inbox runners already know their own commit outcome and use observed mode to tell the unit about it.

### Independent units, and the join keyed on the resource

Every `BeginAsync` opens a new, independent unit. Two units from one factory share nothing: each has its own resource, its own registrations, its own outcome, and each drains only its own `OnCompleted` work. Beginning on a *different* resource while another unit is open is neither joined nor refused — it is simply a second unit, which is what a caller who begins twice asked for.

Propagation — a callee running inside the caller's transaction — is keyed on the resource both hold, never on the factory or a scope. The rules, identical for a `DbContext` and a `DbConnection`:

| Call on an object that already carries a live unit | Result |
|---|---|
| `RunAsync(db, …)` / `RunAsync(connection, …)` | **Joins.** The block receives the owner's handle, runs inside the owner's transaction (and, for EF, outside any execution strategy of its own), and neither commits nor rolls back. A fault propagates to the owner's block, which is what unwinds the unit. The `isolation` argument is ignored: the block runs at the owner's isolation level. A joined block that completes, rolls back, or disposes the owner's unit itself is refused with `InvalidOperationException` once it returns — the owner decides the outcome; hand the callee the owner instead of the unit if it must decide. |
| `BeginAsync(db)` / `BeginAsync(connection)` | **Refused**, naming the join and the accessor. An owning handle over someone else's transaction has no honest semantics. |
| `Enlist(db, tx)` / `Enlist(connection, tx)` | **Refused**, same message. |
| Any EF entry point (`BeginAsync(db)`, `Enlist(db, tx)`, `RunAsync(db, …)`) on a context whose *connection* a raw-ADO unit owns | **Refused** with an EF-specific message: the context would begin a second transaction on the connection the ADO unit already owns, so an EF block can neither own nor join it. Begin the EF unit first and let the ADO code join it from the connection, or run the work as an ADO block on that unit. |

This is the `REQUIRED`-style propagation an ambient design gives for free, without the ambient: a service that wraps its own writes in `RunAsync` composes under any caller that opened the transaction on the same context or connection, and a callee that would rather hold the handle reads it from the object instead. There are no child views — a joined block sees the owner's own `IUnitOfWork`, so `State`, registrations, and `unit.Outbox` / `unit.Jobs` all mean exactly what they mean for the owner.

### The `DbContext` and `DbConnection` bindings

`Headless.UnitOfWork.EntityFramework`'s `BeginAsync(db)`, `Enlist(db, tx)`, and `RunAsync(db, …)` record a binding from the `DbContext` instance to the unit (a `ConditionalWeakTable`, evicted once the unit reaches a terminal state) and expose it as `db.UnitOfWork()`. The raw-ADO providers do the same for the `DbConnection` (`connection.UnitOfWork()`, in `Headless.UnitOfWork`), and the EF provider *also* binds the connection beneath its context, so a raw-ADO helper handed `db.Database.GetDbConnection()` — a Dapper repository, a bulk insert — joins the EF unit through `RunAsync(connection, …)` or reads it through `connection.UnitOfWork()`. The reverse (an EF entry point on a context whose connection an ADO unit already owns) is refused with the EF-specific message in the table above.

A binding also watches for an **owned** unit whose transaction ended without going through the unit — a pooled context reset, a transaction disposed by hand. The next read (`db.UnitOfWork()`, a `RunAsync` join, a `BeginAsync` refusal check) sees the resource's `IsTransactionCompleted`, abandons that unit (it becomes `Failed`, and any handle still held over it refuses registrations with `ObjectDisposedException`), and evicts the binding, so the next entry point begins fresh instead of running writes on a dead transaction and reporting success. An observed-mode unit is left alone by this check: its transaction normally ends at the caller's own commit, before `CompleteAsync` drains.

These bindings are how code that was handed only the resource reaches the unit that owns its transaction: the Headless save pipeline reads `db.UnitOfWork()` to decide whether a caller-owned transaction has a unit; a domain-event handler resolved during that save reads it to enlist an outbox publish (`db.UnitOfWork()?.Outbox.PublishAsync(…)`); a repository given a context or connection reads it the same way. A context created through `IDbContextFactory<T>` needs no special treatment — the binding is on the object, not on any scope.

### The failure hook and rollback

`IUnitOfWork.OnFailed(Func<UnitOfWorkFailure, ValueTask>)` runs after the unit reaches its failed terminal state — an explicit `RollbackAsync`, a dispose without completing (abandon), or a commit fault. `UnitOfWorkFailure.Reason` is one of `RolledBack`, `Abandoned`, or `Faulted`; `Exception` carries the originating fault when one exists. Failure-hook faults are logged and never propagated — a cleanup callback must not be able to mask or replace the real failure.

`RollbackAsync()` is idempotent and legal in both modes: in owned mode it rolls the resource back; in observed mode it records that the caller's own transaction rolled back (and, like `CompleteAsync`, suppresses the forgotten-completion warning). A commit fault transitions the unit to `Failed` *before* the exception propagates, so a second `CompleteAsync` throws the "already failed" message rather than attempting to re-commit — this is the fix for a class of bug where a caller retries a commit whose outcome is actually unknown.

There is no leak detection at scope disposal, because no scope owns a unit. A unit that is begun and never completed or disposed simply holds its transaction open until the handle is collected or the connection dies; `await using` the handle is the discipline, and the only one.

### Typed features on a unit (`GetFeature`)

A bridge package can expose behavior on a unit of work that the unit-of-work packages know nothing about, without either side referencing the other. It registers a **singleton** service that implements the `IUnitOfWorkFeature` marker, and `IUnitOfWork.GetFeature<TFeature>()` resolves that type from the host container the factory was built in, returning `null` when the host registered none. The marker is the whole gate: only opted-in types resolve, so the unit is not a service locator.

- A lookup, not a registration: `GetFeature` itself creates and caches nothing per unit, and a completed unit still resolves it. A feature therefore holds no unit; the handle it is used with arrives as an argument per call. A bridge that binds per-unit state (the outbox binding, the Jobs receivers) keeps it through `GetOrAdd`, so the first accessor read creates it and later reads return it — and a terminal unit refuses the accessor, since `GetOrAdd` is a registration.
- Features are singletons by contract, and the contract is enforced: `AddUnitOfWork()` records the collection it was called on, and `GetFeature` reads the feature's registered lifetime from it before resolving. A scoped or transient registration throws `InvalidOperationException` naming the type and the lifetime — in every environment, not only where `ValidateScopes` is on (the development default, off in production), and for transients, which scope validation never catches. A bridge that needs per-operation state binds it to the handle (a feature method that takes the unit and returns a small bound facade — the shape `unit.Jobs` uses) rather than to a scope.
- `GetFeature` throws `ObjectDisposedException` on a disposed handle and the lifetime refusal above, nothing else. A factory constructed outside DI resolves no features and checks no lifetimes.

The first-party features are Messaging's enlisted outbox (`AddHeadlessMessaging` registers `IUnitOfWorkOutbox`; `Headless.Messaging.Abstractions` surfaces it as `unit.Outbox`) and Jobs' enlisted receivers (`AddHeadlessJobs` registers `IUnitOfWorkJobs`; `Headless.Jobs.Abstractions` surfaces them as `unit.Jobs`, `unit.TimeJobs<T>()`, and `unit.CronJobs<T>()`).

### Handle liveness: registrations answer for the handle

`State` reports the unit's lifecycle, and with no nested views it is the handle's lifecycle too. Registrations are still the contract for attaching work: `OnCompleted`, `OnFailed`, and `GetOrAdd` on a unit that reached a terminal state throw `InvalidOperationException`, and every member throws `ObjectDisposedException` after the handle is disposed. An enlisted publish relies on this — the outbox writer's first act on the caller's handle is a registration, so a dead handle is refused before any row is stored.

### `TransactionEnlistment` and the Jobs guarantee matrix

`TransactionEnlistment { Optional = 0 (default), Required = 1 }`, in `Headless.UnitOfWork.Abstractions`, states whether a **Jobs** write may run through the autonomous receiver or must run through the enlisted one, resolved **per call > per function > host default**, strictest wins. It is Jobs-only. Enlistment itself is decided by the receiver, never by this value: `unit.Jobs` always writes inside the unit's transaction, and an injected scheduler always writes autonomously. `Required` only lets a function declare that the autonomous receiver is not acceptable for it, so a schedule that would otherwise land outside a transaction fails before any effect. Messaging shares none of this — enlistment there is the receiver (`IBus`/`IQueue` versus `unit.Outbox`), and nothing in Messaging reads this enum.

#### Guarantee Matrix

| Receiver | `Optional` (default) | `Required` |
|---|---|---|
| `unit.Jobs` / `unit.TimeJobs<T>()` / `unit.CronJobs<T>()` over a unit with a live, same-database relational resource | Write happens on the unit's transaction; dispatch, scheduler restart, and notification deferred to after commit | Same |
| `unit.Jobs` over a resource-less unit, or one whose resource is dead or belongs to another database | Throws | Throws |
| Injected `IJobScheduler` / `ITimeJobManager<>` / `ICronJobManager<>` | Autonomous durable write; the poller picks it up | Throws |

The enlisted receivers never downgrade: a unit that cannot host the write is refused with a message naming the fix, rather than silently written autonomously. The autonomous receivers never inspect a unit. Composition across host/function/call tiers is strictest-wins (`Required` > `Optional`); see `jobs.md`.

Messaging's counterpart is not a matrix over this enum: an enlisted publish is refused whenever the configured storage cannot join the given unit, and an autonomous publish never consults a unit at all. See `messaging.md#delivery-modes`.

### Message catalogue

Every illegal transition throws with a message naming the remedy — never a bare `InvalidOperationException`:

| Condition | Message |
|---|---|
| `BeginAsync(db)` / `Enlist(db, tx)` on a context that already carries a live unit | `This DbContext already carries an active unit of work. Run the block with RunAsync(db, …) to join it, or pass that unit to the code that needs it (read it with db.UnitOfWork()), instead of beginning a second one on the same context.` |
| `BeginAsync(connection)` / `Enlist(connection, tx)` on a connection that already carries a live unit (the ADO providers, and EF for the connection beneath its context) | `This connection already carries an active unit of work. Run the block with RunAsync(connection, …) to join it, or pass that unit to the code that needs it (read it with connection.UnitOfWork()), instead of beginning a second one on the same connection.` |
| `BeginAsync(db)` with an existing transaction on the context | `The DbContext already has an active transaction. Begin the unit of work before beginning the transaction, or call IUnitOfWorkFactory.Enlist(db, transaction) for a transaction you commit yourself.` |
| `BeginAsync(db)` under a retrying execution strategy | EF's own text, plus: `Use IUnitOfWorkFactory.RunAsync(db, …) to run the unit of work as a retriable block.` |
| `CompleteAsync` after `Completed` | `The unit of work has already completed. Begin a new unit of work for further work.` |
| `CompleteAsync` after `Failed` | `The unit of work has already failed ({Reason}) and cannot be completed. Begin a new unit of work.` |
| `OnCompleted`/`OnFailed`/`GetOrAdd` after a terminal state | `The unit of work is {State}; registrations are accepted only while it is Active.` |
| A member call after dispose | `ObjectDisposedException("UnitOfWork")` |
| A caller-owned transaction saved with integration events but no unit bound to the context | `SaveChanges ran inside a caller-owned transaction that no unit of work owns, so integration events and jobs would dispatch non-atomically. Begin the unit of work on this context (IUnitOfWorkFactory.BeginAsync(db)) before beginning the transaction, or enlist the transaction with Enlist(db, transaction).` |
| `unit.Outbox` in a host that never called `AddHeadlessMessaging` | `No messaging outbox is registered for this unit of work. Call AddHeadlessMessaging during startup to register it.` |
| `unit.Jobs` in a host that never called `AddHeadlessJobs` | `No Jobs feature is registered for this unit of work. Call AddHeadlessJobs during startup to register it.` |
| An enlisted publish into a unit the messaging storage cannot join | `Publishing '{MessageType}' cannot join the active unit of work ({Mismatch}): {detail}. {advice}` — for example `… (MissingRelationalCapability): the active unit of work exposes no relational resource for the messaging storage to write into. Begin the unit of work over a relational resource for the messaging database, or publish without coordination.` |
| Scheduling with `TransactionEnlistment.Required` through an injected scheduler or manager | `Scheduling '{Function}' requires a unit of work (TransactionEnlistment.Required), so it cannot run through an injected scheduler or manager. Schedule it through unit.Jobs on the unit of work the write must join, or register the function with TransactionEnlistment.Optional.` |
| `unit.Jobs` over a unit with no live relational resource | `Scheduling '{Function}' through unit.Jobs requires the unit of work to carry a live relational resource for the job store, but this one has none. Begin the unit of work on the job store's database (BeginAsync(db) or RunAsync(db, …)), or schedule through an injected scheduler for an autonomous write.` |
| `unit.Jobs` over an incompatible or dead resource | `The active unit of work's transaction belongs to another database or is no longer live (closed, completed, or changed), so the Jobs write cannot enlist. Use the same database, or schedule through an injected scheduler for an autonomous write.` |
| A relational unit of work, but the configured Jobs provider can't write inside it | `An active unit of work has a joinable relational resource but the configured job persistence provider does not support coordinated writes. The coordinated-enqueue path requires the EF Core operational store (UseEntityFramework).` |
| Observed unit disposed without `CompleteAsync`/`RollbackAsync` after its transaction finished | `A unit of work enlisted with Enlist(...) was disposed without CompleteAsync or RollbackAsync after its transaction completed; the after-commit work was discarded and durable rows will be recovered by the relay.` (warning) |

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
|---|---|---|---|
| `Headless.UnitOfWork.EntityFramework` | EF Core owns the transaction (`DbContext`). | The unit of work is raw ADO. | `BeginAsync(db)` cannot run under a retrying execution strategy — use `RunAsync(db, …)` there. Never references `Headless.EntityFramework` (the dependency flows the other way), so it stays usable by any EF consumer. |
| `Headless.UnitOfWork.PostgreSql` | Raw `NpgsqlConnection` transactions. | EF owns the transaction (use the EF provider). | No commit edge to observe: observed mode is fully explicit — the caller must call `CompleteAsync`/`RollbackAsync` itself, or a forgotten completion is logged. No execution-strategy retry (a raw-ADO concept has none). |
| `Headless.UnitOfWork.SqlServer` | Raw `SqlConnection` transactions. | EF owns the transaction (use the EF provider). | Same explicit-completion contract as PostgreSQL. |
| The resource-less core (`Headless.UnitOfWork`, no provider) | A coordination window with no transaction of its own — a test harness, or a script whose post-commit work should drain once at the end. | Any case that needs a joinable relational resource — a relational write cannot enlist in a resource-less unit, and a resource-bearing begin underneath it is an independent unit, not a participant. | No relational write enlists on it; each resource-bearing operation underneath opens and commits its own transaction. Messaging's in-memory storage is the one participant that can join it, through its buffer — which is what the test harness relies on. |

---

## Headless.UnitOfWork.Abstractions

Defines the public unit-of-work contracts without provider dependencies: the singleton factory entry point, the unit handle, the resource seams, the `IUnitOfWorkFeature` marker bridge packages opt into, and Jobs' `TransactionEnlistment` knob.

### API and behavior

- `IUnitOfWorkFactory` (singleton): the resource-less `BeginAsync(options?, ct)`, plus the provider primitives — the resource-factory `BeginAsync` (hidden from IntelliSense) and observed-mode `Enlist` (hidden).
- `IUnitOfWork`: `State`, `Failure`, `Resource`, `OnCompleted(Func<ValueTask>)`, `OnFailed(Func<UnitOfWorkFailure, ValueTask>)`, `GetOrAdd<TState>` (both overloads), `GetFeature<TFeature>()`, `PreventRetry()` / `IsRetryPrevented`, `CompleteAsync(ct)`, idempotent `RollbackAsync()`, dispose both ways.
- `IUnitOfWorkFeature`: the marker a bridge's singleton feature service implements so `GetFeature` can hand it out. See [Typed features on a unit](#typed-features-on-a-unit-getfeature).
- `IUnitOfWorkResource` (`IsOwned`, `IsTransactionCompleted`, `CommitAsync`, `RollbackAsync`) and `IRelationalUnitOfWorkResource` (`Connection`, `Transaction`, non-null while active).
- `UnitOfWorkState` (`Active = 0`, `Completed = 1`, `Failed = 2`); `UnitOfWorkFailure` with `UnitOfWorkFailureReason` (`Unspecified`, `RolledBack`, `Abandoned`, `Faulted`).
- `UnitOfWorkOptions`: intentionally empty today; propagation knobs land here additively.
- `TransactionEnlistment { Optional = 0, Required = 1 }`: Jobs only. See [Guarantee Matrix](#guarantee-matrix).

### Design constraints

The factory is a singleton by decision and keeps no record of the units it opens: the handle is the unit's only identity, and there is no `AsyncLocal` and no scoped slot anywhere. See [Why a handle](#why-a-handle-not-an-ambient-or-scoped-current). Dispose without `CompleteAsync` is an implicit rollback; `RollbackAsync` is how the owner of an observed-mode transaction reports its own rollback and suppresses the forgotten-completion warning. `OnCompleted` callbacks are process-local fast-path dispatchers, never the durability mechanism — durable delivery comes from rows committed in the transaction plus the consumer's recovery sweep. An `OnCompleted` fault after a successful commit leaves the unit `Completed` (the data is durable); a commit fault transitions to `Failed` before the exception propagates, so a retry cannot double-apply.

### Install

```bash
dotnet add package Headless.UnitOfWork.Abstractions
```

### Setup and use

```csharp
using Headless.UnitOfWork;  // IUnitOfWorkFactory and the unit.Outbox / unit.Jobs accessors
// using Headless.Messaging; // only when you pass OutboxOptions

public sealed class PlaceOrderHandler(IUnitOfWorkFactory factory, AppDbContext db)
{
    public async Task<Result<OrderId>> Handle(PlaceOrder cmd, CancellationToken ct)
    {
        await using var unit = await factory.BeginAsync(db, cancellationToken: ct); // the EF provider's overload
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await unit.Outbox.PublishAsync(new OrderPlaced(orderId), ct);              // row inside this unit's transaction
        await unit.Jobs.ScheduleAsync(ExpireReservation, orderId, dueAt, ct);      // job row inside the same transaction
        await unit.CompleteAsync(ct);                                              // commit, then dispatch both
        return Result.Ok(orderId);
    }
}
```

`unit.Outbox` comes from `Headless.Messaging.Abstractions` (implementation in `Headless.Messaging.Core`, registered by `AddHeadlessMessaging`); `unit.Jobs` from `Headless.Jobs.Abstractions` (registered by `AddHeadlessJobs`). Publishing the same message through an injected `IBus`, or scheduling through an injected `IJobScheduler`, would store a standalone row that outlives a rollback of this unit.

### Configuration

None.

### Runtime behavior

None.

---

## Headless.UnitOfWork

Implements the singleton `UnitOfWorkFactory`, the in-process unit engine with the atomic terminal claim, and `AddUnitOfWork()`.

### API and behavior

- `AddUnitOfWork()`: idempotent `TryAddSingleton<IUnitOfWorkFactory>` (the factory captures the collection so `GetFeature` can check feature lifetimes); every consumer setup (`AddHeadlessMessaging`, `AddHeadlessJobs`, `AddHeadlessDbContextServices`, the three UnitOfWork provider setups) calls it, so exactly one registration exists regardless of which setup a host invokes first.
- `connection.UnitOfWork()` (`HeadlessDbConnectionUnitOfWorkExtensions`, an extension on `DbConnection` declared in `System.Data.Common`, so it is in scope wherever the connection type is): the unit bound to a connection by any provider's `BeginAsync`/`Enlist`/`RunAsync` — including the connection beneath an EF context — while it is `Active`, or `null`.
- Independent units: every `BeginAsync` returns a new unit; the factory holds no slot, so consecutive and concurrent begins never interact and a faulted resource begin propagates as-is with nothing to release.
- `OnFailed` drain (log-and-continue) on rollback, abandon, and commit fault; `RollbackAsync` idempotent; a commit fault transitions to `Failed` before the exception propagates.
- An observed unit disposed un-completed after its transaction finished logs the forgotten-completion warning.
- `GetFeature<T>()` resolves an `IUnitOfWorkFeature` singleton from the host container the factory was registered in, and throws `InvalidOperationException` naming the type when that registration is scoped or transient.

### Design constraints

`CompleteAsync` in owned mode commits the resource, then drains `OnCompleted`, then disposes unit-local state; the terminal claim settles synchronously before the drain, so a racing dispose never rolls committed work back. A synchronous `Dispose` claims synchronously and offloads the rollback and failure drain to the thread pool (a captured `SynchronizationContext` can neither deadlock nor stall the disposing thread); `DisposeAsync` awaits the same path inline. Savepoint-blindness is harmless by construction: a row written inside a rolled-back savepoint vanishes and its stale `OnCompleted` callback no-ops — callbacks are process-local accelerators, and durability is the committed row plus the consumer's recovery sweep.

### Install

```bash
dotnet add package Headless.UnitOfWork
```

### Setup and use

```csharp
using Headless.UnitOfWork;

services.AddUnitOfWork();

await using var uow = await factory.BeginAsync(cancellationToken: ct);
uow.OnCompleted(async () => await cache.RemoveAsync(key));
await uow.CompleteAsync(ct);
```

### Configuration

None.

### Runtime behavior

Registers the singleton `IUnitOfWorkFactory`; the factory, engine, and handle types are internal. Repeated calls are idempotent. The factory resolves from the root and from any scope alike.

---

## Headless.UnitOfWork.EntityFramework

Gives a plain EF Core `DbContext` the three unit-of-work entry points it needs — an owned begin, an observed enlist, and an execution-strategy-safe block — and the `db.UnitOfWork()` accessor that hands the bound unit to code that only has the context.

### API and behavior

- `IUnitOfWorkFactory.BeginAsync(db, isolation = ReadCommitted, ct)` — owned mode: rejects a context (or its connection) that already carries a live unit (naming the `RunAsync` join and `db.UnitOfWork()`), a context that already has a transaction (naming `Enlist`), and a retrying execution strategy (naming `RunAsync`); begins the transaction eagerly and records the `DbContext → IUnitOfWork` binding plus the same binding on the connection beneath the context. `CompleteAsync` commits, then drains.
- `IUnitOfWorkFactory.Enlist(db, transaction)` — observed mode for a transaction the caller commits: the unit's verbs are no-ops on the transaction; `CompleteAsync` drains without committing; `RollbackAsync` reports the caller's rollback and suppresses the forgotten-completion warning. Records the same binding, and refuses a context that already carries a live unit.
- `IUnitOfWorkFactory.RunAsync(db, operation, isolation, ct)` (and the `TResult` overload) — on a context that already carries a live unit, **joins** it: the block receives the owner's handle, runs inline outside any execution strategy of its own at the owner's isolation level (`isolation` is ignored), and leaves commit and rollback to the owner — a block that ends the unit itself is refused once it returns. On a context whose connection a raw-ADO unit owns, refused before the strategy runs. Otherwise begin → block → complete inside `db.Database.CreateExecutionStrategy()`. The block receives the unit. A retriable failure before commit replays with a fresh transaction and a fresh unit; once commit has started, or after `PreventRetry()`, the fault is captured and rethrown **outside** the strategy so EF cannot replay a possibly-committed block. A drain fault after a durable commit (an `OnCompleted` callback throwing once the unit is `Completed`) is logged and the block's result is returned — the same policy as the Npgsql/SqlClient `RunAsync` — because surfacing it would invite a retry that double-applies a committed block.
- `db.UnitOfWork()` (`HeadlessDbContextUnitOfWorkExtensions`, an extension on `DbContext` declared in `Microsoft.EntityFrameworkCore`, so it is in scope wherever the context type is) — the unit bound to this context while it is `Active`, or `null`. The Headless save pipeline (in `Headless.EntityFramework`) reads it to find the unit that owns a caller-owned transaction; a domain-event handler or repository handed only the context reads it to enlist.
- `AddEntityFrameworkUnitOfWork()` — idempotent; delegates to `AddUnitOfWork()` and registers nothing else.

### Design constraints

**One live unit per context; `RunAsync` is the join.** A second `BeginAsync(db)` or `Enlist(db, tx)` while a unit is bound and active throws rather than joining: two units cannot own one context's transaction. `RunAsync(db, …)` on that context joins instead, which is the shape a self-wrapping service uses. Once the bound unit reaches a terminal state the binding is evicted and a new begin is accepted.

**The retrying-strategy split is deliberate.** `BeginAsync(db)` throws EF's own retrying-strategy message plus the `RunAsync` remedy because a user-initiated transaction cannot survive a strategy retry. `RunAsync` runs the begin *inside* the strategy, so retries replay the whole block with a fresh unit each attempt; the abandoned attempt's unit is unwound (rolled back) before the replay begins, or the replayed begin would meet a still-open transaction on the same context. The replay filter is: `CompleteAsync` started, or `IsRetryPrevented`, ⇒ rethrow outside the strategy. Reconcile an ambiguous post-commit fault with a client-generated key or another durable idempotency key before retrying the business operation.

**Observed mode is the advanced seam.** The save pipeline and the messaging inbox runners commit their own transactions and already know the outcome; they enlist, commit, then call `CompleteAsync` to drain.

**The binding uses a `ConditionalWeakTable`**, so a pooled context never leaks a stale unit: `db.UnitOfWork()` returns only a unit that is still `Active`; a terminal unit is ignored and evicted, and an owned unit whose EF transaction ended without it (`CurrentTransaction` is null or a different transaction) is abandoned and evicted on the next read, so a reset pooled context begins fresh instead of handing a dead unit to a join. This package never references `Headless.EntityFramework` (the reference flows the other way), so the provider stays usable by any EF consumer.

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
await using var unit = await factory.BeginAsync(db, cancellationToken: ct);
db.Orders.Add(order);
await db.SaveChangesAsync(ct);
await unit.Outbox.PublishAsync(new OrderPlaced(order.Id), ct); // row in this transaction, dispatched after commit
await unit.CompleteAsync(ct);

// Retrying strategy configured? Run the unit as a retriable block instead:
await factory.RunAsync(
    db,
    async (unit, ct) =>
    {
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await unit.Outbox.PublishAsync(new OrderPlaced(order.Id), ct);
    },
    cancellationToken: ct
);

// Handed only the context (a domain-event handler, a repository)? Read the bound unit:
if (db.UnitOfWork() is { } bound)
{
    await bound.Outbox.PublishAsync(new OrderPlaced(order.Id), ct);
}

// Or wrap your own work in RunAsync as if you owned the transaction: under a caller that already began on
// this context, the block joins that unit (receives the same handle, commits nothing itself); with no caller
// unit, it begins and commits its own. Either way the service composes.
public Task ReserveStockAsync(OrderId id, CancellationToken ct) =>
    factory.RunAsync(db, async (unit, ct) =>
    {
        db.Reservations.Add(new Reservation(id));
        await db.SaveChangesAsync(ct);
        await unit.Jobs.ScheduleAsync(new ReleaseReservation(id), dueAt, ct);
    }, cancellationToken: ct);
```

The same shapes apply to a `HeadlessDbContext` and a `HeadlessIdentityDbContext` (in `Headless.EntityFramework`) and to a plain `DbContext` alike: the receiver is always the singleton `IUnitOfWorkFactory`, never the context.

An enlisted publish issued directly inside `RunAsync` keeps the block replayable: the unit is owned, so a retriable failure anywhere in the block replays the whole block with a fresh transaction and a fresh unit, the first attempt's row rolls back, and the replayed block publishes again. A `SaveChangesAsync` inside the block that dispatched domain or integration events ends replay instead, because the save clears the events a replayed block would need to re-dispatch (see the retry rules above). `unit.OnCompleted(async () => await bus.PublishAsync(…))` remains the alternative when a message is a post-commit notification that must not be atomic with the write; its message is lost if the process dies before the callback runs.

### Configuration

None.

### Runtime behavior

`AddEntityFrameworkUnitOfWork()` calls the idempotent `AddUnitOfWork()` (singleton `IUnitOfWorkFactory`) and registers nothing else — no interceptor, no hosted service, no options.

---

## Headless.UnitOfWork.PostgreSql

Runs raw-ADO `NpgsqlConnection` work as a unit of work, so outbox rows and job rows written inside the transaction commit with it, dispatch after it commits, and are discarded when it rolls back.

### API and behavior

- `IUnitOfWorkFactory.BeginAsync(connection, isolation, ct)` — owned mode: begins the transaction on that line (opening the connection when it is closed) and binds the unit to the connection (`connection.UnitOfWork()`); `CompleteAsync` commits and drains, `RollbackAsync` or a dispose without completing rolls back. Refused on a connection that already carries a live unit.
- `IUnitOfWorkFactory.Enlist(connection, transaction)` — observed mode for a transaction you commit yourself; call `CompleteAsync` after your commit or `RollbackAsync` after your rollback. Binds and refuses like `BeginAsync`.
- `IUnitOfWorkFactory.RunAsync(connection, operation, isolation, ct)` — on a connection that already carries a live unit (begun by this provider or by EF over the same connection), **joins** it: the operation receives the owner's handle at the owner's isolation level (`isolation` is ignored), commit stays with the owner, and an operation that ends the unit itself is refused once it returns. Otherwise begin → operation → complete in one call; a throwing operation rolls back and rethrows its own exception.
- `AddPostgreSqlUnitOfWork()` — registers the singleton factory (idempotent; there are no provider options).

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

// factory is the singleton IUnitOfWorkFactory.
await using var unit = await factory.BeginAsync(connection, cancellationToken: ct);
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
await using var unit = factory.Enlist(connection, tx);
// ... raw-ADO work + unit.Outbox publishes ...
await tx.CommitAsync(ct);
await unit.CompleteAsync(ct); // required — nothing completes the unit for you
```

### Configuration

None.

### Runtime behavior

Registers the singleton `IUnitOfWorkFactory` only.

---

## Headless.UnitOfWork.SqlServer

Runs raw-ADO `SqlConnection` work as a unit of work, so outbox rows and job rows written inside the transaction commit with it, dispatch after it commits, and are discarded when it rolls back.

### API and behavior

- `IUnitOfWorkFactory.BeginAsync(connection, isolation, ct)` — owned mode: begins the transaction on that line (opening the connection when it is closed) and binds the unit to the connection (`connection.UnitOfWork()`); `CompleteAsync` commits and drains, `RollbackAsync` or a dispose without completing rolls back. Refused on a connection that already carries a live unit.
- `IUnitOfWorkFactory.Enlist(connection, transaction)` — observed mode for a transaction you commit yourself; call `CompleteAsync` after your commit or `RollbackAsync` after your rollback. Binds and refuses like `BeginAsync`.
- `IUnitOfWorkFactory.RunAsync(connection, operation, isolation, ct)` — on a connection that already carries a live unit (begun by this provider or by EF over the same connection), **joins** it: the operation receives the owner's handle at the owner's isolation level (`isolation` is ignored), commit stays with the owner, and an operation that ends the unit itself is refused once it returns. Otherwise begin → operation → complete in one call; a throwing operation rolls back and rethrows its own exception.
- `AddSqlServerUnitOfWork()` — registers the singleton factory (idempotent; there are no provider options).

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

// factory is the singleton IUnitOfWorkFactory.
await using var unit = await factory.BeginAsync(connection, cancellationToken: ct);
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
await using var unit = factory.Enlist(connection, tx);
// ... raw-ADO work + unit.Outbox publishes ...
await tx.CommitAsync(ct);
await unit.CompleteAsync(ct); // required — nothing completes the unit for you
```

### Configuration

None.

### Runtime behavior

Registers the singleton `IUnitOfWorkFactory` only.
