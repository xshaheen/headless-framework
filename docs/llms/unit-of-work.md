---
domain: Unit of Work
packages: UnitOfWork.Abstractions, UnitOfWork, UnitOfWork.EntityFramework, UnitOfWork.Analyzers
---

# Unit of Work

> An explicit unit of work, opened from a singleton factory, that lets Messaging, Jobs, and other stores write inside the caller's transaction and dispatch only after it commits. Nothing ambient carries it: code reaches the unit through the handle or the object the unit was begun on.

## Orientation

You open a unit of work on the line you choose, do the business work, publish messages and schedule jobs **through the unit**, and complete it. Everything enlisted inside commits atomically with the transaction and dispatches after the commit.

The entry point is `IUnitOfWorkFactory`, a **singleton**. A controller, a consumer, a hosted service, and a background job all inject the same object and call `BeginAsync(...)` or `RunAsync(...)` on it. There is no `Current`. The unit is the `IUnitOfWork` handle the caller holds, and other code reaches it in one of three ways:

- It receives the handle as a parameter.
- It reads the handle from the object the unit was begun on: `db.UnitOfWork()`, `connection.UnitOfWork()`, or `ConsumeContext.UnitOfWork` in a consumer.
- It wraps its own work in `RunAsync(db, …)` or `RunAsync(connection, …)` on that same object, which **joins** the live unit instead of opening a second one. A service that wraps itself this way composes under any caller that already opened the transaction on the same context or connection.

`IUnitOfWorkFactory.BeginAsync(...)` with no resource opens a coordination window with no transaction. The provider packages add `BeginAsync(db, ...)` for EF Core and `BeginAsync(connection, ...)` for raw ADO. These begin the transaction on that line (**owned mode**). `IUnitOfWork.CompleteAsync` commits, then runs the registered post-commit work in order. Disposing without completing rolls back. `Enlist(resource, transaction)` is the advanced entry point for code that already commits the transaction itself (**observed mode**): the caller commits, and `CompleteAsync` only runs the post-commit work.

Pick a provider by what owns the transaction:

- EF Core owns it (`DbContext`): `Headless.UnitOfWork.EntityFramework`.
- Raw ADO on PostgreSQL: `Headless.Sql.PostgreSql`, which ships the `NpgsqlConnection` overloads.
- Raw ADO on SQL Server: `Headless.Sql.SqlServer`, which ships the `SqlConnection` overloads.
- Raw ADO on SQLite: `Headless.Sql.Sqlite`, which ships the `SqliteConnection` overloads.
- No relational resource (a script spanning several independent saves, a test harness): the resource-less `BeginAsync` on `Headless.UnitOfWork`.

The receiver at the call site decides whether a write enlists:

- **Messaging:** `IBus` and `IQueue` are autonomous singletons. A publish through them writes a standalone durable row that survives the caller's rollback. To write the message inside the transaction, publish through the unit: `unit.Outbox.PublishAsync(...)` or `unit.Outbox.EnqueueAsync(...)`, an accessor that `Headless.Messaging.Abstractions` adds to `IUnitOfWork`. It always enlists, and it throws before any effect when the configured storage cannot join the unit. See [messaging.md § Delivery Modes](messaging/publishing.md#delivery-modes).
- **Jobs:** the injected `IJobScheduler`, `ITimeJobManager<>`, and `ICronJobManager<>` are autonomous singletons. A schedule through them writes outside any transaction, and the poller picks it up. To schedule inside the transaction, use `unit.Jobs`, `unit.TimeJobs<T>()`, or `unit.CronJobs<T>()`, accessors that `Headless.Jobs.Abstractions` adds to `IUnitOfWork`. They always enlist, and they throw when the unit carries no live relational resource for the job store. No option or policy overrides the receiver. See [Guarantee Matrix](#guarantee-matrix).
- **Distributed locks:** `IDistributedLock` hands out TTL leases that never enlist, so the lease lives and dies independently of the transaction. For a lock that ends with the transaction, call `unit.TransactionLocks.AcquireAsync(resource, acquireTimeout?)` or `TryAcquireAsync(resource, acquireTimeout?)`, an accessor that `Headless.DistributedLocks.Abstractions` adds and the PostgreSQL and SQL Server lock providers implement. Both return a `TransactionLockHandle`. `TryAcquireAsync` returns `null` on contention. `AcquireAsync` throws `LockAcquisitionTimeoutException` when its wait (30 seconds by default) elapses. `AcquireAllAsync(resources, …)` and `TryAcquireAllAsync(resources, …)` lock several resources in ordinal order, with one wait budget, all or nothing, so two units that lock overlapping sets cannot deadlock. Each form has a synchronous twin (`Acquire`, `TryAcquire`, `AcquireAll`, `TryAcquireAll`) for a `SavingChanges` interceptor, which reads the unit with `db.UnitOfWork()`. The accessor throws, before any command, for a unit with no relational resource, a transaction from another provider, or a unit that is no longer active. A host with only Redis or InMemory locks throws on the accessor. See [distributed-locks.md § Locks inside a unit of work](distributed-locks.md#locks-inside-a-unit-of-work).

See [Choosing a Provider](#choosing-a-provider) for the selection table.

## Agent Rules

- Inject `IUnitOfWorkFactory` anywhere. It is a singleton with no scope-bound state, so a hosted service or another singleton depends on it directly, with no `IServiceScopeFactory`. `IBus`, `IQueue`, `IJobScheduler`, and the Jobs managers are also singletons, and all of them are autonomous.
- To enlist a write, call it on the handle: `unit.Outbox.PublishAsync(...)`, `unit.Jobs.ScheduleAsync(...)`, `unit.TimeJobs<T>().AddAsync(...)`. The injected publishers and schedulers never enlist, whatever scope they came from. Read the accessors at each call site; each binds once per unit. An accessor read on a unit that already completed, failed, or rolled back throws, and a binding kept past that point throws on its next use, before anything is stored.
- To catch an injected call made while you hold a unit, reference `Headless.UnitOfWork.Analyzers`. Its rules (HF2001 to HF2005) report the call and name the enlisted receiver, and a code fix rewrites the one-to-one calls. See [Headless.UnitOfWork.Analyzers](#headlessunitofworkanalyzers).
- To reach the unit in a callee, take the `IUnitOfWork` as a parameter, read it from the `DbContext` or `DbConnection` it was begun on (`db.UnitOfWork()`, `connection.UnitOfWork()`), call `context.GetRequiredUnitOfWork()` in a transactional consumer (it throws when the attempt runs outside an inbox transaction), or wrap the work in `RunAsync(db, …)` or `RunAsync(connection, …)` to join. Do not call `BeginAsync` in a callee to "get one": on a context or connection that already carries a live unit, `BeginAsync` and `Enlist` throw and name the join.
- Never end the unit inside a `RunAsync` block (`CompleteAsync`, `RollbackAsync`, or a dispose). `RunAsync` completes the unit when the block returns and rolls it back when the block throws. An owned block that completed its own unit gets its result back with a logged warning and never replays. An owned block that rolled back or disposed its unit and then returned throws `InvalidOperationException`. A joined block that ends the owner's unit throws once it returns.
- Open every unit yourself. No mediator behavior, endpoint filter, or consumer runtime opens one for you. If a handler needs a unit, call `factory.BeginAsync(...)` or `factory.RunAsync(...)`.
- Two begins are two units. Consecutive or concurrent `BeginAsync` calls on *different* resources each open an independent unit with its own resource, post-commit work, and outcome. The join is keyed on the resource, never on the factory or a DI scope: `RunAsync` on a bound `DbContext` or `DbConnection` joins, `BeginAsync` on one throws, and a begin on any other object is a new unit. A joined block receives the owner's own handle.
- To commit two `DbContext` types over one database atomically, build the second over the first one's connection (the same `DbConnection` in its options, or `SetDbConnection` before first use). The second context then joins the first one's unit on `RunAsync(second, …)`, `second.UnitOfWork()`, or a `HeadlessDbContext` save, by adopting its transaction, and releases it when the unit ends. Two contexts with their own connections are two transactions, even on the same database, and cannot share a unit.
- Call `BeginAsync(...)` or `RunAsync(...)` by default. Use `Enlist(...)` only when your code already commits the transaction and you need the post-commit work to follow that commit. Most application code never calls it.
- Treat `OnCompleted` callbacks as a fast path, never as durability. They are process-local, run once, receive no cancellation token, and are lost on a crash before they run. Durable delivery is the row committed in the transaction plus the relay or the jobs poller. Never make correctness depend on a callback running.
- Use `OnFailed` only to release a non-transactional resource reserved for the commit (a lock, a reservation). Its exceptions are logged, never thrown.
- Open units only through the factory. `BeginAsync`, `Enlist`, and `RunAsync` are extension members on `IUnitOfWorkFactory`. No context, connection, or helper type has a second spelling, and none of them takes a `services:` parameter.
- Under a retrying EF execution strategy, call `factory.RunAsync(db, ...)`, not `BeginAsync(db)`. `BeginAsync(db)` throws there, because a transaction you begin cannot survive a strategy replay. `RunAsync` runs begin, block, and complete inside the strategy and replays only a failure raised before the commit started.
- Load and edit inside a replayable `RunAsync(db, …)` block what the block changes, and begin it on a fresh or cleared context. Before each replay, `RunAsync` clears that context's change tracker, so the block re-reads its rows from the rolled-back database. A context that already tracked entities when `RunAsync` began is never replayed: a transient fault reaches you, as after `PreventRetry()`. A fault raised before the block ran (opening the connection, beginning the transaction) is still retried, and the tracked entities stay.
- Read and edit through a second context only after it joined the unit, inside the block: do the work inside `RunAsync(second, …)`, or call `second.UnitOfWork()` first. The tracker reset covers only the context `RunAsync` was called with. If a joined second context still tracks an entity when the attempt fails, the block does not replay and the fault reaches you. A second context that has not joined yet is invisible to the unit: the block replays, and the second context's later save applies both attempts' changes (two inserted rows, a relative update applied twice). A second context that only ran no-tracking reads, `ExecuteUpdate`, or `ExecuteDelete` leaves the block replayable.
- Turn on raw-ADO replay per provider through one spelling: `RunAsync(NpgsqlDataSource, …)`, or `RunAsync(Func<CancellationToken, ValueTask<SqlConnection>>, …)` (`SqliteConnection` on SQLite). These open a connection per attempt and replay under the call's `retry:` argument, else `UnitOfWorkRetryOptions.RetryStrategy`. Neither replays when both are `null`. `RunAsync(connection, …)` never replays. Set `ShouldHandle = UnitOfWorkRetryOptions.DefaultShouldHandle`, or compose with it, because Polly's own default replays every non-cancellation exception. Issue the block's commands on the connection it receives, never on one held from outside.
- To replay an EF block that lost an optimistic-concurrency check, call `RunAsync(db, operation, isolation, retry, ct)` with `retry: EntityFrameworkConflictRetry.Create()`. The block runs again when its save throws `DbUpdateConcurrencyException`, on an empty change tracker and a fresh transaction, at most three times in total. No host default turns it on, the other `RunAsync(db, …)` overloads never replay a conflict, and a joined block never replays (the conflict reaches the owner). Keep every effect of such a block in the unit's transaction (`unit.Outbox`, `unit.Jobs`): a call through an injected publisher, an HTTP request, or a file write repeats on every attempt. Catch `DbUpdateConcurrencyException` for a block that still conflicts on its last attempt.
- Catch `UnitOfWorkInDoubtException` around a `CompleteAsync` or `RunAsync` whose business operation must not apply twice. It means the commit's outcome is unknown, not that it rolled back. Check the operation's durable idempotency key before retrying. Any other commit exception is a certain rollback.
- Expect replay to stop once a write the replay would not repeat has happened. An enlisted publish or Jobs write issued directly in your own `RunAsync(db, …)` block keeps it replayable: the first attempt's rows roll back, and the replayed block writes them again. Two cases end replay, and the fault then reaches you instead:
  - A `SaveChangesAsync` inside your block that dispatched domain or integration events. The save clears the aggregate's events, so a replay would commit the aggregate without the handlers' rows.
  - A domain-event handler that ran during a `HeadlessDbContext` save. The pipeline cannot see what the handler wrote outside its tracker, so it calls `IUnitOfWork.PreventRetry()` once any handler returns.
  
  After replay stops, reconcile an ambiguous fault with a durable idempotency key instead of retrying blind.
- Use the typed accessors a package ships: `unit.Outbox`, `unit.Jobs`, `unit.TransactionLocks`, `unit.Sequences`, `unit.Leases`, `unit.Idempotency`, `unit.Permissions`, `unit.Settings`, `unit.Features`. `GetFeature<TFeature>()` is for a package author who adds such an accessor, not for application code.

## Core Concepts

### A singleton factory, a handle you hold

`IUnitOfWorkFactory` is a singleton that creates units and keeps no record of them. `BeginAsync` returns an `IUnitOfWork` handle, and that handle is the unit's only identity. Whoever holds it can register post-commit work, enlist writes, and complete or roll back the unit. No ambient state (`AsyncLocal`, a scoped slot) exists, so nothing is lost across an `await`, nothing leaks between requests, and a singleton can depend on the factory safely.

### Owned vs observed mode

`IUnitOfWork` looks the same in both modes.

- **Owned mode** (`BeginAsync(...)`): the unit begins the transaction on that line and owns the commit. `CompleteAsync` commits, then runs `OnCompleted` registrations, then disposes unit-local state. A dispose without `CompleteAsync` rolls back.
- **Observed mode** (`Enlist(resource, transaction)`): you own and commit the transaction. The unit never commits or rolls back the transaction. Call `CompleteAsync` after your commit, or `RollbackAsync` after your rollback. A dispose that follows neither, after the transaction finished, logs a forgotten-completion warning. The durable rows are still recovered by the relay; only the fast-path dispatch is lost.

`IUnitOfWork.CompleteAsync` is the one commit verb in both modes.

### Independent units, and the join keyed on the resource

Every `BeginAsync` opens a new, independent unit. Two units from one factory share nothing: each has its own resource, registrations, and outcome, and each runs only its own `OnCompleted` work. A begin on a *different* resource while another unit is open is a second unit.

A callee joins the caller's transaction through the resource both hold. The rules are the same for a `DbContext` and a `DbConnection`:

| Call on an object that already carries a live unit | Result |
|---|---|
| `RunAsync(db, …)` / `RunAsync(connection, …)` | **Joins.** The block receives the owner's handle and runs inside the owner's transaction (for EF, outside any execution strategy of its own). It neither commits nor rolls back. An exception propagates to the owner's block, which unwinds the unit. The `isolation` argument is ignored: the block runs at the owner's isolation level. A joined block that completes, rolls back, or disposes the owner's unit throws `InvalidOperationException` once it returns. If the callee must decide the outcome, hand it the owner's work instead. |
| `BeginAsync(db)` / `BeginAsync(connection)` | **Throws**, naming the join and the accessor. |
| `Enlist(db, tx)` / `Enlist(connection, tx)` | **Throws**, same message. |
| `RunAsync(db, …)`, `db.UnitOfWork()`, or a `HeadlessDbContext` save on a context with no unit of its own, built over the connection of a context that has one | **Joins.** The context receives the owner's handle and adopts the transaction if needed. Its writes commit or roll back with the owner's. The unit releases adoptions it created when the unit ends. A context that already uses the same transaction joins and keeps ownership of its wrapper. A context that uses a different transaction throws. |
| `BeginAsync(db)` / `Enlist(db, tx)` on such a second context | **Throws**, naming the join. The context is left without the unit's transaction. |
| Any EF entry point (`BeginAsync(db)`, `Enlist(db, tx)`, `RunAsync(db, …)`) on a context whose *connection* a raw-ADO unit owns | **Throws** with an EF-specific message. Begin the EF unit first and let the ADO code join it from the connection, or run the work as an ADO block on that unit. |

### The `DbContext` and `DbConnection` bindings

`BeginAsync(db)`, `Enlist(db, tx)`, and `RunAsync(db, …)` bind the `DbContext` to the unit, and `db.UnitOfWork()` returns it. The raw-ADO providers bind the `DbConnection` the same way (`connection.UnitOfWork()`). The EF provider also binds the connection beneath its context, so a raw-ADO helper handed `db.Database.GetDbConnection()` (a Dapper repository, a bulk insert) joins the EF unit through `RunAsync(connection, …)` or reads it through `connection.UnitOfWork()`.

Both accessors return an `Active` unit or `null` when no unit remains bound. Accessors and new work on a bound resource throw `InvalidOperationException` while its unit is `Completing`. A binding also detects an **owned** unit whose transaction ended without going through the unit, such as a pooled context reset or a transaction disposed by hand. The next read abandons that unit (it becomes `Failed`, and a handle still held over it throws `ObjectDisposedException` on registrations), so the next entry point begins fresh instead of writing on a dead transaction. The binding retains an observed unit after its transaction ends so its owner can call `CompleteAsync` after committing or `RollbackAsync` after rolling back. `RunAsync` refuses to join the finished transaction before it runs the block.

A domain-event handler resolved during a `HeadlessDbContext` save reads the binding to enlist (`db.UnitOfWork()?.Outbox.PublishAsync(…)`), and a repository handed a context or connection reads it the same way. A context created through `IDbContextFactory<T>` needs no special treatment, because the binding is on the object, not on a scope. One context per module over one shared connection commits several modules and their outbox rows in one transaction.

### Replay per `RunAsync` overload

Every `RunAsync` follows one replay rule:

- A fault before the commit starts may replay the whole block on a fresh unit and transaction.
- A fault once the commit started (it may already be durable), or after `IUnitOfWork.PreventRetry()`, never replays and reaches the caller.
- An `OnCompleted` fault after a durable commit is logged, and the block's result is returned.
- A block that joins a live unit never replays on its own. It runs again only when its owner replays.

What differs per overload is whether a replay can happen and what classifies the fault:

| Overload | Replays | Classified by |
|---|---|---|
| `RunAsync(DbContext, …)` | Yes, when the context's execution strategy retries | EF Core's strategy (`EnableRetryOnFailure` or a custom one). `UnitOfWorkRetryOptions` does not apply. |
| `RunAsync(DbContext, operation, isolation, retry, …)` with a `retry:` policy | Yes: under the execution strategy, and also when the policy handles the fault. `EntityFrameworkConflictRetry.Create()` handles `DbUpdateConcurrencyException`. | EF Core's strategy for transient faults, and the call's `retry:` for what it handles. No host default. `null` leaves the strategy alone. |
| `RunAsync(NpgsqlDataSource, …)` | Yes, when a policy is configured. Each attempt opens its own connection. | The call's `retry:`, else `UnitOfWorkRetryOptions.RetryStrategy`. Off when both are `null`. |
| `RunAsync(Func<CancellationToken, ValueTask<SqlConnection>>, …)` / `RunAsync(Func<CancellationToken, ValueTask<SqliteConnection>>, …)` | Same as the data-source overload. The factory returns a new connection per attempt. | Same |
| `RunAsync(NpgsqlConnection, …)` / `RunAsync(SqlConnection, …)` / `RunAsync(SqliteConnection, …)` | Never, because you own the connection and it cannot be replaced after it fails. | — |

Replay is off by default, because it repeats any non-transactional effect in the block (an HTTP call, a file write) on every attempt. `UnitOfWorkRetryOptions.DefaultShouldHandle` replays what a whole-transaction replay can cure:

- A failure the driver reports as transient, such as a dropped connection.
- A serialization failure (SQLSTATE `40001`, SQL Server `3960`) or a deadlock (`40P01`, SQL Server `1205`).
- On SQL Server, the error numbers EF Core's `EnableRetryOnFailure` replays (connection drops, Azure throttling, pool limits), read across every error the exception carries, plus a refused, reset, or dropped connection.
- On SQLite, `SQLITE_BUSY` and `SQLITE_LOCKED`.

An EF block and a raw-ADO block on the same database therefore replay the same faults. It never replays a cancellation, a constraint violation, a client-side command timeout (SQL Server `-2`, which may have completed on the server), or anything that is not a database fault. Serializable workloads (CockroachDB, or PostgreSQL at `Serializable`) raise `40001` as normal operation and need this replay.

To write your own retry loop around `RunAsync`, classify faults with `RelationalTransientFaults.IsTransient(exception, cancellationToken)` from `Headless.UnitOfWork`, the same classification. Your loop must still never retry a fault raised by the commit, which may have succeeded on the server.

A lost optimistic-concurrency check is not transient: the database answered, and the same save would lose again. Only re-reading the row and deciding again can succeed, which the EF `retry:` overload does with `EntityFrameworkConflictRetry.Create()`. See [Headless.UnitOfWork.EntityFramework](#headlessunitofworkentityframework).

### The failure hook and rollback

`IUnitOfWork.OnFailed(Func<UnitOfWorkFailure, ValueTask>)` runs after the unit fails: an explicit `RollbackAsync`, a dispose without completing, or a commit fault. `UnitOfWorkFailure.Reason` is `RolledBack`, `Abandoned`, `Faulted`, or `InDoubt`. `Exception` carries the originating fault when one exists. A `RunAsync` block that throws before its commit is rolled back explicitly on every provider, so its `OnFailed` sees `RolledBack`. `Abandoned` means a handle was disposed without a completion verb. `OnFailed` exceptions are logged and never thrown, so a cleanup callback cannot hide the real failure.

`RollbackAsync()` is idempotent and legal in both modes. In owned mode it rolls back. In observed mode it records that your own transaction rolled back and suppresses the forgotten-completion warning. A commit fault moves the unit to `Failed` before the exception propagates, so a second `CompleteAsync` throws the "already failed" message instead of committing again.

#### Commit outcome guarantees

Once `CompleteAsync` claims the unit, `State` is `Completing` while the commit and resource cleanup settle. Enlistment and registration require `Active`. On a successful commit, the state becomes `Completed` before `OnCompleted` runs. On a commit fault, it becomes `Failed` and exposes `Failure` before `OnFailed` runs.

A commit lands, fails for certain, or leaves its outcome unknown. The caller of `CompleteAsync`, or of any `RunAsync`, learns which from the exception type:

| Commit outcome | Unit | `CompleteAsync` / `RunAsync` | `OnCompleted` | `OnFailed` | Enlisted `unit.Outbox` / `unit.Jobs` rows |
|---|---|---|---|---|---|
| Committed | `Completed` | Returns. An `OnCompleted` fault after the commit is logged by `RunAsync` and by a pipeline-owned `SaveChanges`, and thrown by `CompleteAsync`. | Runs | — | Durable. Dispatched now, or by the relay and the poller. |
| Definite failure: the database answered the commit with an error (a `40001` at a `Serializable` commit, a deferred constraint), or the commit was never sent | `Failed(Faulted)` | Throws the commit's own exception | Never | Receives `Faulted` | Rolled back |
| In doubt: the commit operation failed without establishing whether the transaction committed | `Failed(InDoubt)` | Throws `UnitOfWorkInDoubtException` (an `InvalidOperationException`) with the original fault as `InnerException`, and the factory logs a warning (event ID 7). A `HeadlessDbContext` pipeline-owned `SaveChanges` reports its own commit the same way. | Never | Receives `InDoubt` | Committed or rolled back with the transaction. If they committed, the relay and the poller deliver them. |

`RunAsync` never replays a failure from `CompleteAsync`. After `Faulted`, the transaction did not commit, so retrying the business operation is safe. After `InDoubt`, a retry can apply the operation twice: check its durable idempotency key first (`IIdempotentOperations.PeekAsync`, or admit the key again) and retry only when the first attempt is known not to have committed. Enlisted outbox and job rows need no reconciliation: they share the transaction's fate.

Before a Headless-owned PostgreSQL commit, `CompleteAsync` and a pipeline-owned `SaveChanges` check transaction health with one extra query. If an earlier statement aborted the transaction, the check surfaces PostgreSQL error `25P02` so the unit rolls back and skips `OnCompleted`. A fault on the check occurs before the framework sends the commit. For an observed transaction you commit yourself, report success to `CompleteAsync` only after confirming that it committed.

The classification errs toward in doubt, because a false in-doubt costs one idempotency check while a false rollback invites a double apply. In doubt covers a transport fault (`IOException`, `SocketException`, `TimeoutException`) under the driver's exception, a driver fault with no server state that the driver marks transient, a SqlClient timeout (`-2`) or connection-closing severity (class 20 and above), a SQLSTATE in class `08` or `57P`, and a cancellation that arrives while the commit is in flight. A token already cancelled when the commit begins sends nothing, so that is a definite failure.

EF Core also runs notifications and connection cleanup in its commit call. If the call faults after the provider transaction has ended, the outcome is in doubt. Once the commit operation returns successfully, resource cleanup failures are logged and completion callbacks still run. `RunAsync` preserves the original outcome even if disposing its per-attempt connection fails. A pipeline-owned `SaveChanges` returns its saved count if post-commit tracker cleanup fails and logs that the affected context should be discarded.

Synchronous `SaveChanges` starts asynchronous domain handlers and unit callbacks on the thread pool when the caller has a synchronization context or a custom task scheduler. Use `SaveChangesAsync` when the caller must remain responsive during those callbacks.

Always `await using` the handle. No scope owns a unit, so a unit that is never completed or disposed holds its transaction open until the handle is collected or the connection dies.

### Typed features on a unit (`GetFeature`)

A package exposes behavior on a unit through a **singleton** service that implements the `IUnitOfWorkFeature` marker. `IUnitOfWork.GetFeature<TFeature>()` resolves that type from the container the factory was built in, or returns `null` when none is registered. Only types that implement the marker resolve. A scoped or transient feature registration throws `InvalidOperationException` naming the type and the lifetime, in every environment. `GetFeature` throws `ObjectDisposedException` on a disposed handle. A factory constructed outside DI resolves no features. To write a feature that binds state to the unit, take the handle as a method argument and store the state with `unit.GetOrAdd(...)`, the shape `unit.Jobs` uses.

The first-party features and the setup call that registers each:

| Accessor | Package | Registered by | See |
|---|---|---|---|
| `unit.Outbox` | `Headless.Messaging.Abstractions` | `AddHeadlessMessaging` | [messaging.md § Delivery Modes](messaging/publishing.md#delivery-modes) |
| `unit.Jobs`, `unit.TimeJobs<T>()`, `unit.CronJobs<T>()` | `Headless.Jobs.Abstractions` | `AddHeadlessJobs` | [Guarantee Matrix](#guarantee-matrix) |
| `unit.TransactionLocks` | `Headless.DistributedLocks.Abstractions` | `UsePostgreSql` or `UseSqlServer` on `AddHeadlessDistributedLocks` | [distributed-locks.md § Locks inside a unit of work](distributed-locks.md#locks-inside-a-unit-of-work) |
| `unit.Sequences` | `Headless.Sequences.Abstractions` | `AddHeadlessSequences` | |
| `unit.Leases` | `Headless.Fencing.Abstractions` | `AddHeadlessFencing` | |
| `unit.Idempotency` | `Headless.Idempotency.Abstractions` | `AddHeadlessIdempotency` | |
| `unit.Permissions` | `Headless.Permissions.Abstractions` | `AddHeadlessPermissions` | [permissions.md § Writing grants inside a unit of work](permissions.md#writing-grants-inside-a-unit-of-work). Clears the grant cache only after the unit commits. |
| `unit.Settings` | `Headless.Settings.Abstractions` | `AddHeadlessSettings` | [settings.md § Writing settings inside a unit of work](settings.md#writing-settings-inside-a-unit-of-work) |
| `unit.Features` | `Headless.Features.Abstractions` | `AddHeadlessFeatures` | [features.md § Writing feature values inside a unit of work](features.md#writing-feature-values-inside-a-unit-of-work) |

All accessors are in the `Headless.UnitOfWork` namespace.

### Handle liveness

`State` reports the unit's lifecycle. `OnCompleted`, `OnFailed`, and `GetOrAdd` on a unit that reached a terminal state throw `InvalidOperationException`, and every member throws `ObjectDisposedException` after the handle is disposed. An enlisted publish registers on the handle first, so a dead handle is refused before any row is stored.

### The Jobs guarantee matrix

A Jobs write enlists when, and only when, it goes through `unit.Jobs`, `unit.TimeJobs<T>()`, or `unit.CronJobs<T>()`. No per-call option, per-function policy, or host default changes that, as with Messaging (`IBus` and `IQueue` versus `unit.Outbox`).

#### Guarantee Matrix

| Receiver | Write |
|---|---|
| `unit.Jobs` / `unit.TimeJobs<T>()` / `unit.CronJobs<T>()` over a unit with a live, same-database relational resource | On the unit's transaction. Dispatch, scheduler restart, and notification wait until after the commit. |
| `unit.Jobs` over a resource-less unit, or one whose resource is dead or belongs to another database | Throws |
| Injected `IJobScheduler` / `ITimeJobManager<>` / `ICronJobManager<>` | Autonomous durable write in the store's own transaction. The poller picks it up. |

The enlisted receivers never fall back to an autonomous write: a unit that cannot host the write throws with a message naming the fix. The autonomous receivers never inspect a unit. A job chain is atomic through either receiver: with the unit, or in the store's own transaction.

Messaging follows the same rule: an enlisted publish throws whenever the configured storage cannot join the unit, and an autonomous publish never consults a unit. See [messaging.md § Delivery Modes](messaging/publishing.md#delivery-modes).

### Database identity and several databases

A host has one Jobs store and one primary messaging storage, plus one additional messaging outbox for each other database registered with `setup.AddOutbox(name)`, each name unique within the host. An enlisted write lands only when the unit's connection reaches a database its receiver is configured for. The relational messaging storages and the EF Jobs store decide "same database" with one check:

- The connection type must match.
- The database name must match exactly.
- The data source must match after a conservative normalization: the host compares case-insensitively, a `tcp:` or `tcp://` prefix is ignored, and `localhost`, `127.0.0.1`, `::1`, `[::1]`, `.`, and `(local)` name the same host. Ports, instance names, and Unix-socket paths compare exactly.
- A multi-host Npgsql source (`Host=pg1,pg2`) matches when the unit's connection reaches one of the listed hosts, each with its inline port or the shared `Port`. The database name must still match.
- On SQLite, files compare by their real path: both the configured path and the path an open connection reports have every symbolic link along them resolved, so a store and a unit that reach one file through different links (the macOS temp directory, `/var` and `/private/var`) share it. A relative path resolves against the process working directory, as SQLite opens it. On Windows the comparison ignores case. An in-memory or `file:` URI data source names no comparable file and is refused.

So spell a database the same way in every connection string. A false match would commit a row into a database whose relay or poller never reads it, so the check refuses anything it cannot prove and names the mismatch.

With several `DbContext` types, each on its own database, register an additional outbox for each database whose units publish: `setup.AddOutbox("billing").UsePostgreSqlEntityFramework<BillingDb>()` (or `UseSqlServerEntityFramework<BillingDb>()`). `unit.Outbox` then writes to the outbox whose database matches the unit, and a unit on a database with no outbox throws. Two outboxes, the primary included, that resolve to the same database fail host startup. The inbox, retry state, and dashboard stay on the primary storage. See [messaging.md § Additional outboxes](messaging/storage.md#additional-outboxes).

Jobs has no per-database store. `unit.Jobs` enlists only on the Jobs database. A unit on another database that must schedule a job atomically publishes a message through `unit.Outbox` and schedules the job from that message's consumer, with the consumer's own unit on the Jobs database. Publishing from `OnCompleted` or after the commit also works when the loss window of an autonomous write is acceptable.

### Message catalogue

Every illegal transition throws with a message that names the fix:

| Condition | Message |
|---|---|
| `BeginAsync(db)` / `Enlist(db, tx)` on a context that already carries a live unit | `This DbContext already carries an active unit of work. Run the block with RunAsync(db, …) to join it, or pass that unit to the code that needs it (read it with db.UnitOfWork()), instead of beginning a second one on the same context.` |
| `BeginAsync(db)` / `Enlist(db, tx)` on a second context whose connection carries another context's live unit | `The connection beneath this DbContext already carries an active unit of work begun on another DbContext over the same connection. Run the block with RunAsync(db, …) to join it, or read it with db.UnitOfWork(), instead of beginning a second one on the same connection.` |
| A second context that already uses a different transaction on the unit's connection | `The connection beneath this DbContext carries an active unit of work, but the context already uses a different transaction, so it cannot join that unit. Do not begin a transaction on a context that shares a unit of work's connection; let it join the unit through RunAsync(db, …) or db.UnitOfWork().` |
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
| `unit.Features` in a host that never called `AddHeadlessFeatures` | `No features storage is registered for this unit of work. Call AddHeadlessFeatures during startup with UseEntityFramework, UsePostgreSql, or UseSqlServer.` |
| `unit.Settings` in a host that never called `AddHeadlessSettings` | `No settings storage is registered for this unit of work. Call AddHeadlessSettings during startup with UseEntityFramework, UsePostgreSql, or UseSqlServer.` |
| `unit.Permissions` in a host that never called `AddHeadlessPermissions` | `No permissions storage is registered for this unit of work. Call AddHeadlessPermissions during startup with UseEntityFramework, UsePostgreSql, or UseSqlServer.` |
| An enlisted publish into a unit the messaging storage cannot join | `Publishing '{MessageType}' cannot join the active unit of work ({Mismatch}): {detail}. {advice}`. For example: `… (MissingRelationalCapability): the active unit of work exposes no relational resource for the messaging storage to write into. Begin the unit of work over a relational resource for the messaging database, or publish without coordination.` |
| `unit.Jobs` over a unit with no live relational resource | `Scheduling '{Function}' through unit.Jobs requires the unit of work to carry a live relational resource for the job store, but this one has none. Begin the unit of work on the job store's database (BeginAsync(db) or RunAsync(db, …)), or schedule through an injected scheduler for an autonomous write.` |
| `unit.Jobs` over an incompatible or dead resource | `The active unit of work's transaction belongs to another database or is no longer live (closed, completed, or changed), so the Jobs write cannot enlist. Use the same database, or schedule through an injected scheduler for an autonomous write.` |
| A relational unit of work, but the configured Jobs provider cannot write inside it | `An active unit of work has a joinable relational resource but the configured job persistence provider does not support coordinated writes. The coordinated-enqueue path requires the EF Core operational store (UseEntityFramework).` |
| A commit whose connection failed after it may have reached the database | `UnitOfWorkInDoubtException`: `The unit of work's commit may have reached the database, but the connection failed before the outcome was known, so the transaction may or may not have committed. Check the operation's durable idempotency key before retrying it; the inner exception is the driver's fault.` The factory also logs a warning (event ID 7) that names `IIdempotentOperations.PeekAsync` as the check. |
| Observed unit disposed without `CompleteAsync`/`RollbackAsync` after its transaction finished | `A unit of work enlisted with Enlist(...) was disposed without CompleteAsync or RollbackAsync after its transaction completed; the after-commit work was discarded and durable rows will be recovered by the relay.` (warning) |

## For store authors

Read this section only when you write storage that joins a caller's unit of work, such as a custom outbox, a job store, or a relational store whose writes take an `IUnitOfWork`. Application code never calls these members. All of them are in namespace `Headless.UnitOfWork`, and each refusal is an `InvalidOperationException` thrown before any command runs, naming what the caller must change. Pass `owner` (your store's package) and `operation` (what the call is, such as `"fenced lease"`); both appear only in those messages.

| Member | Package | Use it to |
| --- | --- | --- |
| `UnitOfWorkTransactions.RequireTransaction<TTransaction>(unitOfWork, operation)` | `Headless.UnitOfWork.Abstractions` | Get the unit's live provider transaction for an operation that must run inside it, such as a transaction-scoped lock or a gap-free sequence. |
| `RelationalEnlistment.RequireLive(unitOfWork, connectionType, transactionType, owner, operation)` | `Headless.UnitOfWork` | Get the unit's open connection and live transaction, checked against your provider's types. |
| `RelationalEnlistment.RequireSameDatabase(...)` | `Headless.UnitOfWork` | Refuse a unit whose connection reaches a database your store is not configured for. |
| `RelationalDatabaseIdentity.IsSameDatabase(...)` | `Headless.UnitOfWork` | Ask the same question without throwing. |
| `EntityFrameworkEnlistment.RunAsync(dbFactory, unitOfWork, owner, operation, work, cancellationToken)` | `Headless.UnitOfWork.EntityFramework` | Run your store's EF Core work on a context that borrows the unit's connection and transaction for one call. |

### Require a live transaction

`RequireTransaction<TTransaction>` returns the unit's transaction as `TTransaction`. It throws `ArgumentNullException` for a null unit, and `InvalidOperationException` when the unit is not `Active`, carries no `IRelationalUnitOfWorkResource`, its transaction already completed, or the transaction is not a `TTransaction`.

`RequireLive` makes the same checks and returns `(DbConnection Connection, DbTransaction Transaction)`. It also refuses a transaction that is not a `transactionType`, a connection that is not an open `connectionType`, and a transaction no longer bound to the connection, which also catches a committed or rolled-back transaction.

Run `RequireLive` on every enlisted call, not only the first. The caller may have ended the transaction or closed the connection since, and a statement on either would run outside the unit.

### Refuse another database

A store must check that the unit's connection reaches the database it is configured for, because a row committed into another database is never read by the store's relay or poller. The check is the one in [Database identity and several databases](#database-identity-and-several-databases): same connection type, exact database name, and a conservatively normalized data source. A store that is configured from a connection string builds an unopened connection for the comparison. The check never opens it.

| Overload | Behavior |
| --- | --- |
| `RelationalDatabaseIdentity.IsSameDatabase(configured, candidate)` | Returns `false` when the two differ, or when either leaves the data source or database name empty. Throws `ArgumentNullException` for a null connection. |
| `RelationalDatabaseIdentity.IsSameDatabase<TConfiguration>(unitOfWork, configuration, createConfigured, candidate)` | The same answer, with a per-unit cache. See below. |
| `RelationalEnlistment.RequireSameDatabase(configured, candidate, owner, operation)` | Throws `InvalidOperationException` naming both databases and data sources when they differ. |
| `RelationalEnlistment.RequireSameDatabase<TConfiguration>(unitOfWork, configuration, createConfigured, candidate, owner, operation)` | The throwing form with the per-unit cache. |

**Per-unit cache.** A store checks the same unit on every enlisted call, and neither side changes during the unit's life. The overloads that take `unitOfWork` and `configuration` remember a match on the unit, so later calls with the same configuration and connection in that unit return without calling `createConfigured` or comparing again. `configuration` is the cache key, compared by reference, so pass your store's long-lived options or store object, which builds the same connection for as long as the unit lives. Only a match is cached. A unit that is no longer active is checked without the cache.

### Run EF Core work inside the unit

`EntityFrameworkEnlistment.RunAsync<TContext, TResult>` creates a context from your store's `IDbContextFactory<TContext>`, points it at the unit's connection and transaction, runs `work`, then gives the context its own connection back before disposing it, so a pooled context does not keep a connection it does not own. The unit can be an EF Core unit or a raw-ADO unit on the same database. `work` must not commit, roll back, or change the context's connection. `RunAsync` throws `InvalidOperationException` when:

- the unit is not active or carries no live relational transaction (the `RequireLive` checks);
- the context's options supply a connection the context owns. Configure the context with a connection string, or with `contextOwnsConnection: false`;
- the context retries on failure and no execution strategy is already running. Run the unit with `IUnitOfWorkFactory.RunAsync(db, …)`, so the whole unit replays under the strategy;
- the context's database differs from the unit's (the `RequireSameDatabase` check).

The context releases the unit's transaction even when `work` is cancelled.

**Verify:** in an integration test, begin a unit, call your store's enlisted write, and roll the unit back. The row is absent. Begin a unit on a second database and assert that the write throws `InvalidOperationException` naming both databases.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
|---|---|---|---|
| `Headless.UnitOfWork.EntityFramework` | EF Core owns the transaction (`DbContext`). | The unit is raw ADO. | `BeginAsync(db)` cannot run under a retrying execution strategy. Use `RunAsync(db, …)` there. Works with any `DbContext`, not only `HeadlessDbContext`. |
| `Headless.Sql.PostgreSql` | Raw `NpgsqlConnection` transactions. | EF owns the transaction (use the EF provider). | In observed mode you must call `CompleteAsync` or `RollbackAsync` yourself, or a forgotten completion is logged. Replay only through `RunAsync(NpgsqlDataSource, …)`, which opens a connection per attempt. |
| `Headless.Sql.SqlServer` | Raw `SqlConnection` transactions. | EF owns the transaction (use the EF provider). | Same explicit completion as PostgreSQL. Replay only through `RunAsync(connectionFactory, …)`, because SqlClient ships no `DbDataSource`. |
| `Headless.Sql.Sqlite` | Raw `SqliteConnection` transactions, for the Headless stores that have a SQLite provider. | Many concurrent writers: SQLite admits one writer per file. | Same explicit completion as PostgreSQL. Every unit begins `IMMEDIATE` and holds the database write lock until it ends. Replay only through `RunAsync(connectionFactory, …)`. |
| The resource-less core (`Headless.UnitOfWork`, no provider) | A coordination window with no transaction of its own: a test harness, or a script whose post-commit work should run once at the end. | Any relational write must enlist: none can enlist on a resource-less unit, and a resource-bearing begin inside it is an independent unit. | Each resource-bearing operation inside opens and commits its own transaction. Messaging's in-memory storage is the one participant that can join it. |

---

## Headless.UnitOfWork.Abstractions

The public unit-of-work contracts: the singleton factory, the unit handle, the resource contracts, and the `IUnitOfWorkFeature` marker.

### Setup

```bash
dotnet add package Headless.UnitOfWork.Abstractions
```

```csharp
using Headless.UnitOfWork;  // IUnitOfWorkFactory and the unit.Outbox / unit.Jobs accessors
// using Headless.Messaging; // only when you pass OutboxOptions

public sealed class PlaceOrderHandler(IUnitOfWorkFactory factory, AppDbContext db)
{
    public async Task<OrderId> Handle(PlaceOrder cmd, CancellationToken ct)
    {
        await using var unit = await factory.BeginAsync(db, ct); // the EF provider's overload
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await unit.Outbox.PublishAsync(new OrderPlaced(orderId), ct);              // row inside this unit's transaction
        await unit.Jobs.ScheduleAsync(new ExpireReservation(orderId), dueAt, ct);  // job row inside the same transaction
        await unit.CompleteAsync(ct);                                              // commit, then dispatch both
        return orderId;
    }
}
```

`unit.Outbox` comes from `Headless.Messaging.Abstractions` (registered by `AddHeadlessMessaging`), and `unit.Jobs` from `Headless.Jobs.Abstractions` (registered by `AddHeadlessJobs`). Publishing the same message through an injected `IBus`, or scheduling through an injected `IJobScheduler`, would store a standalone row that outlives a rollback of this unit.

**Verify:** in an integration test, throw before `CompleteAsync` and assert that neither the order row nor the outbox row exists, then complete and assert `unit.State == UnitOfWorkState.Completed` and that both rows exist. A missing `AddHeadlessMessaging` or `AddHeadlessJobs` throws on the first `unit.Outbox` or `unit.Jobs` read (see the [Message catalogue](#message-catalogue)).

### Design and runtime behavior

- `IUnitOfWorkFactory` (singleton): the resource-less `BeginAsync(ct)`. The provider packages add the resource overloads as extension members.
- `IUnitOfWork`: `State`, `Failure`, `Resource`, `OnCompleted(Func<ValueTask>)`, `OnFailed(Func<UnitOfWorkFailure, ValueTask>)`, `GetOrAdd<TState>` (two overloads), `GetFeature<TFeature>()`, `PreventRetry()` and `IsRetryPrevented`, `CompleteAsync(ct)`, idempotent `RollbackAsync()`, and both dispose forms.
- `IUnitOfWorkFeature`: the marker a package's singleton feature implements. See [Typed features on a unit](#typed-features-on-a-unit-getfeature).
- `IUnitOfWorkResource` (`IsOwned`, `IsTransactionCompleted`, `CommitAsync`, `RollbackAsync`) and `IRelationalUnitOfWorkResource` (`Connection`, `Transaction`, non-null while active). Cast `unit.Resource` to `IRelationalUnitOfWorkResource` to issue raw commands on the unit's transaction.
- `UnitOfWorkState` (`Active`, `Completed`, `Failed`). `UnitOfWorkFailure` with `UnitOfWorkFailureReason` (`Unspecified`, `RolledBack`, `Abandoned`, `Faulted`, `InDoubt`). `UnitOfWorkInDoubtException`, which `CompleteAsync` throws for an in-doubt commit. See [Commit outcome guarantees](#commit-outcome-guarantees).

---

## Headless.UnitOfWork

The singleton `IUnitOfWorkFactory` implementation, `AddUnitOfWork()`, the host replay options, and the `DbConnection` accessor.

### Setup

```bash
dotnet add package Headless.UnitOfWork
```

```csharp
using Headless.UnitOfWork;

services.AddUnitOfWork();

await using var uow = await factory.BeginAsync(cancellationToken: ct);
uow.OnCompleted(async () => await cache.RemoveAsync(key));
await uow.CompleteAsync(ct);
```

**Verify:** resolve `IUnitOfWorkFactory` from the built provider. A host without `AddUnitOfWork()` (or a setup that calls it) fails resolution with `InvalidOperationException`. In a test, assert that the `OnCompleted` callback ran after `CompleteAsync` and did not run after a dispose without it.

### Configuration

`UnitOfWorkRetryOptions`, through the options pattern. It applies only to `RunAsync(NpgsqlDataSource, …)` and to the connection-factory `RunAsync` overloads on SQL Server and SQLite. A call's own `retry:` argument overrides it.

```csharp
using Polly;
using Polly.Retry;

services.Configure<UnitOfWorkRetryOptions>(options =>
    options.RetryStrategy = new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(50),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = UnitOfWorkRetryOptions.DefaultShouldHandle,
    }
);
```

| Option | Default | Effect |
|---|---|---|
| `RetryStrategy` | `null` | No replay. When set, Polly decides how often and after what delay a faulted attempt replays, and validates the options on the first replaying call. Delays wait real time, not the application's `TimeProvider`. |
| `DefaultShouldHandle` (static) | — | The framework's transient classification. Compose it into a custom `ShouldHandle` instead of replacing it. |

### Design and runtime behavior

- `AddUnitOfWork()`: idempotent registration of the singleton `IUnitOfWorkFactory`. `AddHeadlessMessaging`, `AddHeadlessJobs`, `AddHeadlessDbContextServices`, and `AddEntityFrameworkUnitOfWork` all call it, so one registration exists whichever setup runs first. It also registers the options infrastructure, so `services.Configure<UnitOfWorkRetryOptions>(…)` works before or after it. The factory resolves from the root and from any scope.
- `UnitOfWorkRetryOptions`: the host's default replay policy for the per-attempt-connection `RunAsync` overloads. See [Configuration](#configuration).
- `connection.UnitOfWork()`: an extension on `DbConnection` declared in `System.Data.Common`, so it is in scope wherever the connection type is. It reads the binding created by any provider's `BeginAsync`, `Enlist`, or `RunAsync`, including the connection beneath an EF context. See [The `DbContext` and `DbConnection` bindings](#the-dbcontext-and-dbconnection-bindings).
- `RelationalTransientFaults.IsTransient(exception, cancellationToken)`: the replay classification that `UnitOfWorkRetryOptions.DefaultShouldHandle` applies. Use it in a hand-written retry loop. See [Replay per `RunAsync` overload](#replay-per-runasync-overload).
- A synchronous `Dispose` of an uncompleted unit blocks until the rollback and the `OnFailed` callbacks finish, so code that keeps using the same context or connection after the dispose never races the rollback. It cannot deadlock on a captured `SynchronizationContext`. `DisposeAsync` does the same work asynchronously.
- A row written inside a savepoint that rolled back disappears, and its `OnCompleted` callback has nothing to dispatch. Durability is the committed row, not the callback.

---

## Headless.UnitOfWork.EntityFramework

Unit-of-work entry points for any EF Core `DbContext`: an owned begin, an observed enlist, an execution-strategy-safe block, and the `db.UnitOfWork()` accessor for code that only has the context.

### Setup

```bash
dotnet add package Headless.UnitOfWork.EntityFramework
```

```csharp
using System.Data;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

services.AddDbContext<MyDbContext>(options => options.UseNpgsql(connectionString));
services.AddEntityFrameworkUnitOfWork();

// Owned mode: the transaction begins on this line; CompleteAsync commits and drains.
await using var unit = await factory.BeginAsync(db, ct);
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
public sealed class StockService(IUnitOfWorkFactory factory, MyDbContext db)
{
    public Task ReserveStockAsync(OrderId id, DateTimeOffset dueAt, CancellationToken ct) =>
        factory.RunAsync(db, async (unit, ct) =>
        {
            db.Reservations.Add(new Reservation(id));
            await db.SaveChangesAsync(ct);
            await unit.Jobs.ScheduleAsync(new ReleaseReservation(id), dueAt, ct);
        }, cancellationToken: ct);
}

// Read, decide, and save under a concurrency token? Opt in to a replay when another writer got there first.
// The block re-reads the row on every attempt, and the publish is in the transaction, so it happens once.
await factory.RunAsync(
    db,
    async (unit, ct) =>
    {
        var account = await db.Accounts.SingleAsync(x => x.Id == accountId, ct);
        account.Withdraw(amount);
        await db.SaveChangesAsync(ct); // DbUpdateConcurrencyException when the row changed since the read
        await unit.Outbox.PublishAsync(new MoneyWithdrawn(accountId, amount), ct);
    },
    IsolationLevel.ReadCommitted,
    retry: EntityFrameworkConflictRetry.Create(),
    ct
);
```

The same calls work on a `HeadlessDbContext`, a `HeadlessIdentityDbContext`, and a plain `DbContext`. The receiver is always the singleton `IUnitOfWorkFactory`, never the context.

For a message that is a post-commit notification and must not be atomic with the write, use `unit.OnCompleted(async () => await bus.PublishAsync(…))`. The message is lost if the process dies before the callback runs.

**Verify:** in an integration test, assert `db.UnitOfWork()` is the handle `BeginAsync(db, ct)` returned while the unit is open, and `null` after `CompleteAsync`. Calling `BeginAsync(db, ct)` on a context configured with `EnableRetryOnFailure` throws `InvalidOperationException` naming `RunAsync`, which proves the strategy is in effect.

### Design and runtime behavior

- **Overloads come in pairs, never with an optional isolation level.** One overload begins at `ReadCommitted`. The other takes an explicit `IsolationLevel` before the token. Pass the token positionally: `BeginAsync(db, ct)`, `RunAsync(db, operation, ct)`, `BeginAsync(db, IsolationLevel.Serializable, ct)`. A third overload, `(…, isolation, retry, ct)`, takes a per-call replay policy. To use it at `ReadCommitted`, pass `IsolationLevel.ReadCommitted` explicitly. This applies to every provider's `BeginAsync` and `RunAsync`.
- `IUnitOfWorkFactory.BeginAsync(db, ct)` and `BeginAsync(db, isolation, ct)`: owned mode. Begins the transaction on that line and binds the context and its connection to the unit. Throws for a context (or its connection) that already carries a live unit, a context that already has a transaction, and a retrying execution strategy, each message naming the fix. `CompleteAsync` commits, then runs the post-commit work.
- `IUnitOfWorkFactory.Enlist(db, transaction)`: observed mode for a transaction you commit. `CompleteAsync` runs the post-commit work without committing. `RollbackAsync` reports your rollback. Binds and throws like `BeginAsync`.
- `IUnitOfWorkFactory.RunAsync(db, operation, ct)` and `RunAsync(db, operation, isolation, ct)`, with `TResult` overloads. On a context that already carries a live unit, the block joins it (see the join table in [Independent units](#independent-units-and-the-join-keyed-on-the-resource)). On a context whose connection a raw-ADO unit owns, it throws before the strategy runs. Otherwise it runs begin, block, and complete inside `db.Database.CreateExecutionStrategy()`. A retriable failure before the commit rolls the attempt back (`OnFailed` sees `RolledBack`) and replays with a fresh transaction and unit. Once the commit started, or after `PreventRetry()`, the fault is rethrown outside the strategy, so EF cannot replay a block that may have committed. An `OnCompleted` fault after a durable commit is logged and the block's result is returned, because throwing would invite a retry that applies a committed block twice.
- `IUnitOfWorkFactory.RunAsync(db, operation, isolation, retry, ct)`, with a `TResult` overload: the same block with a Polly `RetryStrategyOptions` for this call. A fault the policy handles before the commit started rolls the attempt back, waits the policy's delay in real time, clears the change tracker, and runs the block again. `retry: null` behaves as the overload without it. The policy cannot override a refusal: there is no replay once the commit started, after `PreventRetry()`, after the block ended its own unit, for a joined block, when the context tracked entities before the call, or when a joined second context still tracks an entity. A cancelled token ends the loop. A block that exhausts the policy throws its last fault unchanged.
- `EntityFrameworkConflictRetry.Create(maxAttempts = 3)`: the policy for a lost optimistic-concurrency check. It handles `DbUpdateConcurrencyException` only, runs the block at most `maxAttempts` times in total (two or more), and waits a jittered exponential delay between attempts (25 ms base, 500 ms ceiling). Each call returns a new `RetryStrategyOptions` you may adjust. `EntityFrameworkConflictRetry.DefaultShouldHandle` is its predicate, for a policy you build yourself. When each of N writers contending for one row must succeed, set `maxAttempts` to at least N: each lost attempt means another writer committed.
- `db.UnitOfWork()`: an extension on `DbContext` declared in `Microsoft.EntityFrameworkCore`, so it is in scope wherever the context type is. Returns the unit bound to this context while it is `Active`, else `null`. On a second context built over a bound context's connection, it returns that unit and adopts its transaction.
- `AddEntityFrameworkUnitOfWork()`: idempotent. Calls `AddUnitOfWork()` and registers nothing else: no interceptor, no hosted service, no options.

**One live unit per context, and `RunAsync` is the join.** A second `BeginAsync(db)` or `Enlist(db, tx)` while a unit is bound and active throws. `RunAsync(db, …)` on that context joins instead. Once the bound unit ends, a new begin is accepted.

**A `retry:` policy and a retrying strategy add, never multiply.** The policy's replays count for the whole call. With a policy of `MaxRetryAttempts` P and a strategy that retries R times, the block runs at most 1 + P + R times: `EntityFrameworkConflictRetry.Create()` (P = 2) under `EnableRetryOnFailure()` at its default of six retries runs at most nine times. Once the policy's replays are spent, a later fault goes to the strategy, which retries it only if it is transient.

**Conflict replay needs a concurrency token and a block that reads inside itself.** The replay cures a conflict only because the block re-reads the row. An entity loaded before `RunAsync` keeps its stale token, and a context that tracked anything before the call does not replay at all. Without a concurrency token (`IsConcurrencyToken()`, `IsRowVersion()`, or the provider's native row version), EF Core raises no conflict and the last writer silently wins, so the policy has nothing to handle. The second-context rules in [Agent Rules](#agent-rules) apply here too.

**A pooled context never carries a stale unit.** `db.UnitOfWork()` returns only an `Active` unit. An owned unit whose EF transaction ended without it (`CurrentTransaction` is `null` or a different transaction) is abandoned on the next read, so a reset pooled context begins fresh.

---

## Raw ADO on PostgreSQL

The raw-ADO overloads ship in [`Headless.Sql.PostgreSql`](sql.md#headlesssqlpostgresql), beside its connection factory, and register through the core `AddUnitOfWork()`. Outbox rows and job rows written inside the transaction commit with it, dispatch after it commits, and are discarded when it rolls back.

### Setup

```bash
dotnet add package Headless.Sql.PostgreSql
```

```csharp
using Headless.UnitOfWork;
using Npgsql;

services.AddUnitOfWork();

// factory is the singleton IUnitOfWorkFactory.
await using var unit = await factory.BeginAsync(connection, ct);
var relational = (IRelationalUnitOfWorkResource)unit.Resource!;
await using (var command = new NpgsqlCommand("INSERT INTO orders (id) VALUES (@id)", connection, (NpgsqlTransaction)relational.Transaction))
{
    command.Parameters.AddWithValue("id", orderId);
    await command.ExecuteNonQueryAsync(ct);
}
await unit.Outbox.PublishAsync(new OrderPlaced(orderId), ct); // enlisted; an injected IBus would not be
await unit.CompleteAsync(ct);
```

A replayable block, for a `Serializable` workload that expects `40001`. Each attempt gets its own connection; issue every command on it:

```csharp
await factory.RunAsync(
    dataSource,
    async (unit, connection, ct) =>
    {
        var tx = (NpgsqlTransaction)((IRelationalUnitOfWorkResource)unit.Resource!).Transaction;
        await using var command = new NpgsqlCommand("UPDATE accounts SET balance = balance - 10 WHERE id = @id", connection, tx);
        command.Parameters.AddWithValue("id", accountId);
        await command.ExecuteNonQueryAsync(ct);
        await unit.Outbox.PublishAsync(new AccountDebited(accountId), ct); // rolled back and written again on a replay
    },
    IsolationLevel.Serializable,
    ct // no retry argument, so the host's UnitOfWorkRetryOptions applies
);
```

Observed mode, for a transaction you own:

```csharp
await using var tx = await connection.BeginTransactionAsync(ct);
await using var unit = factory.Enlist(connection, tx);
// ... raw-ADO work + unit.Outbox publishes ...
await tx.CommitAsync(ct);
await unit.CompleteAsync(ct); // required — nothing completes the unit for you
```

**Verify:** in an integration test, assert `connection.UnitOfWork()` returns the unit while it is open, and that a dispose without `CompleteAsync` leaves neither the `orders` row nor the outbox row. In observed mode, a dispose after your commit without `CompleteAsync` logs the forgotten-completion warning.

### Design and runtime behavior

- `IUnitOfWorkFactory.BeginAsync(connection, ct)` and `BeginAsync(connection, isolation, ct)`: owned mode. Begins the transaction on that line, opening a closed connection, and binds the unit to the connection (`connection.UnitOfWork()`). `CompleteAsync` commits and runs the post-commit work. `RollbackAsync` or a dispose without completing rolls back. Throws on a connection that already carries a live unit.
- `IUnitOfWorkFactory.Enlist(connection, transaction)`: observed mode for a transaction you commit yourself. Call `CompleteAsync` after your commit or `RollbackAsync` after your rollback; nothing completes the unit for you. Binds and throws like `BeginAsync`.
- `IUnitOfWorkFactory.RunAsync(connection, operation, ct)` and the `isolation` overload: on a connection that already carries a live unit (begun by this provider, or by EF over the same connection), joins it. Otherwise runs begin, operation, and complete in one call. A throwing operation rolls back and rethrows its own exception. Never replays.
- `IUnitOfWorkFactory.RunAsync(dataSource, operation, ct)`, the `isolation` overload, and the `isolation, retry` overload, with `TResult` overloads: each attempt opens a connection from the `NpgsqlDataSource`, begins an owned unit on it, and hands the block `(unit, connection, ct)`. A fault before the commit that the policy classifies as transient replays the block on a fresh connection, transaction, and unit. The attempt's connection is disposed once its unit ends. The policy is `retry`, else `UnitOfWorkRetryOptions.RetryStrategy`. With neither, the block runs once.
- No provider-specific registration and no provider options. `AddUnitOfWork()` registers the factory.

---

## Raw ADO on SQL Server

The raw-ADO overloads ship in [`Headless.Sql.SqlServer`](sql.md#headlesssqlsqlserver), beside its connection factory, and register through the core `AddUnitOfWork()`. Outbox rows and job rows written inside the transaction commit with it, dispatch after it commits, and are discarded when it rolls back.

### Setup

```bash
dotnet add package Headless.Sql.SqlServer
```

```csharp
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;

services.AddUnitOfWork();

// factory is the singleton IUnitOfWorkFactory; BeginAsync opens a closed connection.
await using var connection = new SqlConnection(connectionString);
await using var unit = await factory.BeginAsync(connection, ct);
var relational = (IRelationalUnitOfWorkResource)unit.Resource!;
await using (var command = new SqlCommand("INSERT INTO orders (id) VALUES (@id)", connection, (SqlTransaction)relational.Transaction))
{
    command.Parameters.AddWithValue("@id", orderId);
    await command.ExecuteNonQueryAsync(ct);
}
await unit.Outbox.PublishAsync(new OrderPlaced(orderId), ct); // enlisted; an injected IBus would not be
await unit.CompleteAsync(ct);
```

A replayable block with a per-call policy (overriding any host default):

```csharp
await factory.RunAsync(
    ct => ValueTask.FromResult(new SqlConnection(connectionString)),
    async (unit, connection, ct) =>
    {
        var tx = (SqlTransaction)((IRelationalUnitOfWorkResource)unit.Resource!).Transaction;
        await using var command = new SqlCommand("UPDATE accounts SET balance = balance - 10 WHERE id = @id", connection, tx);
        command.Parameters.AddWithValue("@id", accountId);
        await command.ExecuteNonQueryAsync(ct);
    },
    IsolationLevel.Snapshot,
    new RetryStrategyOptions { MaxRetryAttempts = 3, ShouldHandle = UnitOfWorkRetryOptions.DefaultShouldHandle },
    ct
);
```

Observed mode, for a transaction you own:

```csharp
using Microsoft.Data.SqlClient;

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync(ct);
await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
await using var unit = factory.Enlist(connection, tx);
// ... raw-ADO work + unit.Outbox publishes ...
await tx.CommitAsync(ct);
await unit.CompleteAsync(ct); // required — nothing completes the unit for you
```

**Verify:** the same assertions as on [PostgreSQL](#raw-ado-on-postgresql): `connection.UnitOfWork()` returns the open unit, and a dispose without `CompleteAsync` leaves no `orders` row and no outbox row.

### Design and runtime behavior

- `BeginAsync(connection, …)`, `Enlist(connection, transaction)`, and `RunAsync(connection, operation, …)` behave as on [PostgreSQL](#raw-ado-on-postgresql), with `SqlConnection` and `SqlTransaction`.
- `IUnitOfWorkFactory.RunAsync(connectionFactory, operation, …)`, with the `isolation` and `isolation, retry` overloads and `TResult` overloads: `connectionFactory` is a `Func<CancellationToken, ValueTask<SqlConnection>>` that returns a **new** connection per attempt (a closed one is opened). Each attempt begins an owned unit on it and hands the block `(unit, connection, ct)`. Replay, policy, and disposal behave as in the PostgreSQL data-source overload. SqlClient ships no `DbDataSource`, so the overload takes a delegate.
- No provider-specific registration and no provider options.

---

## Raw ADO on SQLite

The raw-ADO overloads ship in [`Headless.Sql.Sqlite`](sql.md#headlesssqlsqlite), beside its connection factory, and register through the core `AddUnitOfWork()`. Rows that Headless stores write inside the transaction, such as a gap-free sequence value or a fenced lease, commit and roll back with it.

### Setup

```bash
dotnet add package Headless.Sql.Sqlite
```

**Verify:** in a test, open a unit with `BeginAsync(connection, ct)` and assert that a second unit's `BeginAsync` on another connection to the same file fails with `SQLITE_BUSY` after the `Default Timeout`, which proves the `IMMEDIATE` write lock.

### Design and runtime behavior

- `IUnitOfWorkFactory.BeginAsync(connection, …)`: owned mode, as on PostgreSQL. The transaction begins `IMMEDIATE`, so the unit holds the database write lock from this line. A second unit's `BeginAsync` waits for it, up to the connection's `Default Timeout`, then fails with `SQLITE_BUSY`. `Microsoft.Data.Sqlite` runs every isolation level except `ReadUncommitted` as serializable.
- `IUnitOfWorkFactory.Enlist(connection, transaction)`: observed mode, with explicit completion as on PostgreSQL. Begin the transaction with `BeginTransaction()` (immediate). A `deferred: true` transaction that reads before it writes can fail with `SQLITE_BUSY` when another writer got there first.
- `IUnitOfWorkFactory.RunAsync(connection, operation, …)`, with `TResult` overloads: joins a live unit on the connection, else runs begin, operation, and complete. Never replays.
- `IUnitOfWorkFactory.RunAsync(connectionFactory, operation, …)`, with the `isolation, retry` overload and `TResult` overloads: each attempt takes a new `SqliteConnection` from the factory, as on SQL Server. `SQLITE_BUSY` and `SQLITE_LOCKED` before the commit are transient under `UnitOfWorkRetryOptions.DefaultShouldHandle`.
- No provider-specific registration and no provider options. A store and a unit share a database file when their paths resolve to the same real file (see [Database identity](#database-identity-and-several-databases)).

---

## Headless.UnitOfWork.Analyzers

Roslyn analyzers that report a write made through an autonomous service while a unit of work is in scope, and name the enlisted receiver on that unit. A code fix rewrites the call where the enlisted call is one-to-one.

### Setup

```bash
dotnet add package Headless.UnitOfWork.Analyzers
```

The package is a development dependency: it adds analyzers to the project that references it and flows to no consumer. It needs Roslyn 5.12 or later, the compiler in the .NET 11 SDK or Visual Studio 2026 18.12 or later, which a `net11.0` application already builds with.

**Verify:** add `dotnet_diagnostic.HF2001.severity = warning` to `.editorconfig` and call `IBus.PublishAsync` inside a `RunAsync(db, (unit, ct) => …)` block. The build reports HF2001.

### When a unit is in scope

A call is reported only when an `IUnitOfWork` local or parameter is visible at the call:

- The unit is a lambda or method parameter (the `RunAsync(db, (unit, ct) => …)` block), or a local such as `var unit = db.UnitOfWork()`, `connection.UnitOfWork()`, `context.GetRequiredUnitOfWork()` or `context.UnitOfWork` in a consumer, or `await factory.BeginAsync(ct)`. A unit captured from an enclosing method or lambda counts at any depth.
- The unit is declared before the call and definitely assigned there.
- The compiler's nullable flow state for the unit at the call is not-null. `db.UnitOfWork()`, `connection.UnitOfWork()`, and `context.UnitOfWork` return `IUnitOfWork?`, so the call counts only after a check such as `if (unit is null) return;`, `?? throw`, or `is { } unit`. `context.GetRequiredUnitOfWork()` returns `IUnitOfWork` and needs none. Where nullable analysis is off, the unit counts.
- Inside a callback passed to `OnCompleted` or `OnFailed` (a lambda, or a local function passed as a method group), a unit declared outside the callback does not count: the callback runs after the transaction ends, where the autonomous call is correct and the enlisted receiver refuses the write.
- Inside a `static` lambda or local function, an outer unit does not count.
- After a statement that always runs before the call and completes, rolls back, or disposes the unit, the unit does not count. A rollback inside an early-exit branch (`if (!valid) { await unit.RollbackAsync(); return; }`) leaves the unit live on the path that reaches the call, so the call is still reported.
- Fields, properties, and primary-constructor parameters are not units in scope.
- When several units are eligible, the message names the one declared last.

### Known limits

- An enlisted Jobs receiver that reaches the call through a parameter, or through a local assigned more than once, is reported, because the analyzer cannot trace it back to `unit.Jobs`. Suppress that call with a reason.
- A unit completed in one branch and rolled back in the other (`if (ok) await unit.CompleteAsync(); else await unit.RollbackAsync();`) still counts after the `if`. Do not apply the fix there; suppress the call with a reason.
- A unit declared as a `foreach` iteration variable is not counted, so calls in the loop body are not reported.
- A local function declared before the unit cannot capture it, so a call inside it is not reported even when the function runs later.
- The analyzer cannot see provider topology. Where a provider refuses enlistment, the enlisted receiver throws: the cache idempotency provider refuses every `unit.Idempotency` call, SQLite idempotency refuses enlisted admission, and `unit.Outbox` refuses a unit on a different database from the messaging storage. On such a host, do not apply the fix: set that rule's severity to `none`, or suppress the call with a reason.
- Where nullable analysis is off, the code fix can write `unit.Outbox` in a branch where the unit is null at run time.

### Severity and suppression

Every rule is a suggestion by default, because some autonomous calls inside a unit are correct: a lock that must outlive the transaction, or a notification that must go out even if the unit rolls back. Raise each rule on its own in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.HF2001.severity = error
dotnet_diagnostic.HF2002.severity = warning
dotnet_diagnostic.HF2003.severity = suggestion
```

Keep a deliberate autonomous call with an inline reason, for example `#pragma warning disable HF2001 // The alert must go out even when the order rolls back.`. No attribute or option marks a call site or a message type as autonomous: the receiver at the call site is the declaration.

### Diagnostics

Every rule reports in category `Headless.UnitOfWork.Analyzers` at `info` severity by default. Each rule's help link points to its row below.

| Rule | Reported when | Fix |
| --- | --- | --- |
| <a id="hf2001"></a>HF2001 | `IBus.PublishAsync` or `IQueue.EnqueueAsync` is called while a unit of work is in scope. | Call `unit.Outbox.PublishAsync` or `unit.Outbox.EnqueueAsync`. The code fix rewrites the `(content, cancellationToken)` overload. The options and builder overloads take options the outbox does not, so they get no fix. |
| <a id="hf2002"></a>HF2002 | An `IJobScheduler`, `ITimeJobManager<T>`, or `ICronJobManager<T>` member that enlists (enqueue, schedule, schedule-after, recurring, keyed schedule, replace, and cancel, and the managers' add members) is called on an injected receiver while a unit of work is in scope. Calls through `unit.Jobs`, `unit.TimeJobs<T>()`, or `unit.CronJobs<T>()`, or through a local initialized once from one, are not reported. Cancel, pause, resume, requeue, update, and delete never enlist and are not reported. | Call the same member on `unit.Jobs`, `unit.TimeJobs<T>()`, or `unit.CronJobs<T>()`. The code fix swaps the receiver and keeps `T`. It offers nothing when `T` has no public parameterless constructor or has `required` members, which the accessors' `new()` constraint rejects. |
| <a id="hf2003"></a>HF2003 | `IDistributedLock.AcquireAsync`, `TryAcquireAsync`, `AcquireAllAsync`, or `TryAcquireAllAsync` is called while a unit of work is in scope. | When the lock should end with the transaction, take it with the same-named member of `unit.TransactionLocks`. The reader-writer and semaphore composites have no transaction-scoped counterpart and are not reported. Keep the autonomous lock when it must outlive the transaction. No code fix: the transaction lock takes an acquire timeout and returns a different handle. |
| <a id="hf2004"></a>HF2004 | `IFencedLeases.GrantAsync`, `RenewAsync`, `SettleAsync`, or `ReleaseAsync` is called while a unit of work is in scope. `SweepExpiredAsync` and `PurgeAsync` have no enlisted counterpart and are not reported. | Call the same member on `unit.Leases`. The code fix swaps the receiver. |
| <a id="hf2005"></a>HF2005 | `IIdempotentOperations.AdmitAsync`, `CompleteAsync`, `SetRecoveryPointAsync`, or `ReleaseAsync`, including the JSON overloads, is called while a unit of work is in scope. `RenewAsync` and `PeekAsync` have no enlisted counterpart and are not reported. | Call the same member on `unit.Idempotency`. The code fix swaps the receiver. |
