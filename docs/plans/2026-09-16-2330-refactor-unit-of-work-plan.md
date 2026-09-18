---
title: Scoped Unit of Work - Plan
type: refactor
date: 2026-09-16
artifact_contract: x-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: session
execution: code
supersedes: 2026-09-13-1536-refactor-explicit-transaction-guarantees-plan.md (Stage 2 surface)
---

# Scoped Unit of Work - Plan

## Goal Capsule

- **Objective:** An application developer opens a unit of work on the line they choose, does business work, publishes messages and enqueues jobs through the services they already inject, and completes it; everything inside is atomic with the transaction and dispatches after commit. Nothing about that flow depends on `AsyncLocal`, a capture-before-await rule, or a `services:` argument, and every misuse fails with a message that names the fix.
- **Means:** Replace `Headless.CommitCoordination.*` with `Headless.UnitOfWork.*`: a **scoped** `IUnitOfWorkManager` whose `Current` is a plain field, an `IUnitOfWork` handle with `CompleteAsync` / `OnCompleted` / `OnFailed`, provider entry points `BeginAsync(db)` / `BeginAsync(connection)` / `BeginAsync()`, scoped facades for `IBus` / `IQueue` / the job managers over their singleton cores, and one shared `TransactionEnlistment { WhenAvailable, Required, Never }` knob replacing `DeliveryMode.Coordinated` and `bool RequireAtomicEnlistment`.
- **Authority:** Session decisions recorded under Key Decisions; repository conventions in `CLAUDE.md`, `CONCEPTS.md`, `docs/authoring/AUTHORING.md`. Breaking changes are in scope (greenfield, user-directed).
- **Execution:** Lands on `refactor/commit-coordination-minimal-contract` (PR #897, stacked on #895) in dependency order U1 to U8; the old packages are deleted only after every consumer has migrated so the branch builds at every unit boundary. Stop and report when evidence contradicts a Key Decision: a downstream consumer that must resolve `IBus` from a singleton, a persisted column that stores `DeliveryMode` numerically, or a conformance scenario that cannot hold without `AsyncLocal` flow.
- **Tail ownership:** The calling workflow owns review, commits, and CI monitoring; the PR is #897 (no new PR).

## Product Contract

### Summary

Developers get one noun they already know (unit of work), one entry point (`IUnitOfWorkManager`), one commit verb (`CompleteAsync`), and dispose-is-rollback. Messaging and Jobs join the active unit of work through the same injected services with no extra parameters. Misuse is loud: a stranded frame is structurally impossible, a leaked unit of work is reported when its scope disposes, and every illegal transition throws a sentence that names the remedy.

### Problem Frame

- Every consumer of the coordination contract is a singleton (`IBus`, `IQueue`, both job managers, `MessagePublisher`) reaching a `static AsyncLocal` (`CommitScopeStack`). The ambient design exists *because* of those lifetimes; it is not a requirement of the problem.
- The ambient rule "capture `Current` synchronously before the first await" is enforced by comments (`JobsManager.CommitCoordination.cs:21-35`, `MessagePublisher.cs:52`) and XML prose (`ICommitScopeFactory.Open`). It produced a recorded critical bug (`docs/solutions/logic-errors/asynclocal-ambient-scope-stranded-across-await.md`) whose happy-path test stayed green.
- `Current` returning `null` is indistinguishable between "no scope configured" and "scope stranded"; the silent branch is the non-atomic one.
- `ExecuteCoordinatedTransactionAsync(…, services: requestServiceProvider)` requires the caller to hand over the request scope because a plain `DbContext` cannot self-source it (`HeadlessDbContextServices.cs:36`: EF's `ApplicationServiceProvider` is the root provider under pooling).
- The same guarantee is spelled two ways: a three-value `DeliveryMode` in Messaging and a `bool RequireAtomicEnlistment` on six Jobs types; in a coordinated scope with no relational handle, Messaging with relational storage throws while Jobs falls back to a direct insert.
- `HeadlessDbContext.SaveChangesAsync()` already opens an implicit transaction and enlists (`HeadlessSaveChangesPipeline.cs:232-250`), but a caller who opened the transaction themselves hits `OutboxIntegrationEventDispatcher._EnsureCoordinated()` and gets an exception telling them to call `Database.EnlistCommitCoordination(transaction, services)`.
- The EF provider carries an interceptor, an options configuration, a startup gate, and a probe-mode option whose only purpose is to observe a commit edge the unit-of-work owner already knows.

### Key Decisions

- KD1. **Scoped, not ambient.** `IUnitOfWorkManager` is a scoped service; `Current` is a field on it. No `AsyncLocal` anywhere in the new packages. (session-settled: user-directed after the ambient-vs-scoped comparison; precedent: MassTransit `ScopedConsumeContextProvider`, Wolverine's removal of `MessageContext.Current` for `ScopedMessageContextHolder` "with a structural, scope-local mechanism that does not depend on async-flow propagation", NServiceBus `TransactionalSessionBase` with three plain fields.)
- KD2. **The developer opens the unit of work explicitly, on the line they choose.** No mediator behavior, endpoint filter, or consumer-runtime opens one on the developer's behalf. (session-settled: user-directed — "I want to be in control about which part of the transaction script to start the transaction scope on".)
- KD3. **The manager is the entry point; the resource is the argument.** `unitOfWork.BeginAsync(db, ct)`, `BeginAsync(connection, ct)`, `BeginAsync(ct)` are extension members on `IUnitOfWorkManager` contributed by the provider packages. No extension on `DbContext` (cannot reliably reach the request scope under pooling) and no `services:` parameter. (session-settled: user-approved shape; precedent: ABP `IUnitOfWorkManager.Begin()` → `IUnitOfWork`.)
- KD4. **Owned mode begins the transaction eagerly and owns commit; observed mode is the advanced seam.** `BeginAsync(resource)` begins the transaction on that line, and `CompleteAsync` commits it then drains. `Enlist(resource, transaction)` observes a transaction the caller commits; the caller calls `CompleteAsync` after its own commit. There is no interceptor-driven third mode. (session-settled: one commit verb; the Headless save pipeline and the inbox runners commit their own transactions and already know the outcome.)
- KD5. **Scoped facades over singleton cores for `IBus`, `IQueue`, `ITimeJobManager<>`, `ICronJobManager<>`, `IJobScheduler`.** The facade reads `IUnitOfWorkManager.Current` and passes it down; the cores become stateless with respect to coordination and take `IUnitOfWork?` as an argument. Framework-internal singletons that must publish (`HybridCache`, `DistributedLock`, `DistributedReadWriteLock`, `DistributedSemaphoreProvider`) keep their public `IBus` constructor parameters; their DI factories stop resolving the scoped `IBus` and pass a unit-less bus built over the internal core (`new Bus(sp.GetRequiredService<MessagePublisher>())`, internal ctor, `InternalsVisibleTo` already granted), which publishes `Direct` exactly as today. `SubscribeExecutor`'s callback publish resolves `IBus` from the attempt scope on the transactional branch and from a scope it opens itself (`provider.CreateAsyncScope()`, no unit of work, autonomous durable) on the non-transactional branch. The Jobs startup seeders run inside `CreateAsyncScope()`. Public rule: a singleton or hosted service that needs one of the five services creates a scope. (precedent: MassTransit scoped `IPublishEndpoint`; cost accepted: a downstream singleton resolving `IBus` gets an MS-DI scope-validation error, which is the correct signal.)
- KD6. **One shared `TransactionEnlistment { WhenAvailable, Required, Never }`** replaces `DeliveryMode.Coordinated` and `bool RequireAtomicEnlistment`. `DeliveryMode` keeps the durability axis only: `Durable` / `Direct`. Precedence for both Messaging and Jobs: per call > per type/function > host default. (precedent: EF `AutoTransactionBehavior { WhenNeeded, Always, Never }`, ABP `UnitOfWorkTransactionBehavior { Auto, Enabled, Disabled }`; Laravel's consumer-declared `ShouldQueueAfterCommit` is the per-type tier.)
- KD7. **One guarantee matrix for Messaging and Jobs.** "No joinable resource" behaves as "no unit of work": `WhenAvailable` writes autonomously, `Required` throws. A joinable resource that is incompatible (other database, dead transaction) throws in both. This removes the Jobs/Messaging asymmetry by rule, not by exception list.
- KD8. **A failure hook returns, as an advanced member, and observed mode gets an explicit rollback verb.** `OnFailed(Func<UnitOfWorkFailure, ValueTask>)` runs after rollback or abandonment; its faults are logged, never propagated. `OnCompleted` faults propagate after the drain. `RollbackAsync()` is idempotent, legal in both modes, and is how an owner that rolled its own transaction back tells the unit so (the inbox runners' shape); it suppresses the forgotten-completion warning. Scope-local state disposal stays on both outcomes. (precedent: every abstraction whose after-commit work is a delegate ships a failure hook — `Transaction.TransactionCompleted` with `Status`, EF `TransactionRolledBack`/`TransactionFailed`, ABP `Failed`; the justified use case is releasing a non-transactional resource reserved in anticipation of commit — Laravel's `ShouldBeUnique` lock release.)
- KD9. **Nesting: join by default, savepoints deferred.** `BeginAsync` while a unit of work is active on the same resource returns a child handle. Child `CompleteAsync` transfers its registrations to the parent; a child disposed without completing drops its registrations and marks the root aborted, so the root's `CompleteAsync` throws; the root also refuses to complete while a child is still active. A resource-bearing `BeginAsync`/`Enlist` under a **resource-less** root (the harness case, and `BeginAsync()` followed by `HeadlessDbContext.SaveChangesAsync()`) opens an *independent nested unit* with its own commit and drain; registrations are not transferred. A different resource while a resource-bearing unit is active throws. Savepoint-backed children are additive later. (precedent: the transfer-on-commit / drop-on-rollback / run-at-root rule that Django, Rails and Laravel independently converged on; TransactionScope's "missing inner Complete aborts root"; ABP's `HasActiveChildUnitOfWorks` guard. Anti-precedent: ABP's `ChildUnitOfWork.CompleteAsync` is a silent no-op and a child `Rollback` poisons the parent without surfacing — issues #2452, #16198.)
- KD10. **Two packages: `Headless.UnitOfWork.Abstractions` (contracts, `TransactionEnlistment`, zero dependencies) and `Headless.UnitOfWork` (the scoped manager, engine, `AddUnitOfWork()`).** `Messaging.Abstractions` and `Jobs.Abstractions` reference only the Abstractions package. Every consumer package that needs the manager (`Headless.EntityFramework`, `Messaging.Core`, `Jobs.Core`) calls the idempotent `AddUnitOfWork()`, so exactly one registration exists and the null-sentinel / last-wins dance disappears. Provider packages: `Headless.UnitOfWork.EntityFramework`, `.PostgreSql`, `.SqlServer`.
- KD11. **The EF interceptor, its options configuration, the startup gate, `CommitProbeMode`, and the `Headless.EntityFramework.CommitCoordination` adapter are deleted.** `Headless.EntityFramework` references `Headless.UnitOfWork.EntityFramework` (for `Enlist(db, tx)`, `RunAsync`, and the context binding); the provider never references `Headless.EntityFramework` (no cycle, same shape as today's `CommitCoordination.EntityFramework`). `Headless.EntityFramework.Messaging` swaps its adapter reference for the provider.
- KD12. **Callbacks stay process-local and never authoritative.** Durability is the row committed in the transaction plus the consumer's recovery sweep; the drain is the fast path. Savepoint-blindness is therefore harmless by construction (a row written inside a rolled-back savepoint vanishes and the stale callback no-ops) and is documented in those words.
- KD13. **The EF provider binds the unit of work to the `DbContext` instance, and the save pipeline adopts it into the context's own scope.** `BeginAsync(db)` / `Enlist(db, tx)` record the binding (`ConditionalWeakTable<DbContext, IUnitOfWork>`, cleared when the unit reaches a terminal state). The Headless save pipeline consults the binding first and the scope manager second; when the bound unit belongs to another scope's manager (a context created by `IDbContextFactory<T>` owns its own scope — `HeadlessDbContextFactory`), the pipeline **adopts** it into its scope's manager for the save's duration (internal `IUnitOfWorkManager.Adopt(IUnitOfWork) : IDisposable`), so domain-event handlers, the outbox dispatcher, and anything else resolved in that scope see the same `Current`. `Adopt` is swap-and-restore and re-entrant (a handler calling `SaveChangesAsync` on the same context re-enters the pipeline while the slot already holds the adopted unit, which is a no-op); adopting while the slot holds a *different* active unit throws the concurrent-begin message. Without this a correct program would either fail the "no unit of work" check or write autonomously inside the caller's open transaction.

### Requirements

- R1. `IUnitOfWorkManager` (scoped) exposes `IUnitOfWork? Current` and the core `BeginAsync(UnitOfWorkOptions?, CancellationToken)`; provider packages add `BeginAsync(DbContext, …)`, `BeginAsync(NpgsqlConnection, …)`, `BeginAsync(SqlConnection, …)` and the observed-mode `Enlist(…)` members.
- R2. `IUnitOfWork` exposes `State`, `Resource` (typed access to the enlisted `IUnitOfWorkResource`, `null` when none), `OnCompleted(Func<ValueTask>) : IDisposable`, `OnFailed(Func<UnitOfWorkFailure, ValueTask>) : IDisposable`, `GetOrAdd<TState>` (both overloads), `PreventRetry()` / `IsRetryPrevented`, `CompleteAsync(CancellationToken)`, `RollbackAsync()` (idempotent; owned mode rolls the resource back, observed mode records the outcome), `Dispose` / `DisposeAsync`.
- R3. `CompleteAsync` in owned mode commits the resource transaction, then drains `OnCompleted` in registration order, then disposes scope-local state; `OnCompleted` faults surface after the drain (one as-is, several as `AggregateException`). In observed mode it drains without committing.
- R4. Dispose without `CompleteAsync` rolls back (owned) or is treated as rolled back (observed): `OnFailed` callbacks run with `UnitOfWorkFailure.Reason = Abandoned`, then scope-local state is disposed. A dispose after `CompleteAsync` is a no-op.
- R5. Illegal transitions throw with the catalogued messages (see Verification Contract): begin on a context that already has a transaction, complete twice, register after terminal, any member after dispose, a second resource while a resource-bearing unit is active, a concurrent begin while another begin is in flight, root complete while a child is active or after an abandoned child, begin under a retrying execution strategy.
- R6. The manager, on its own disposal, rolls back any still-active unit of work, runs its `OnFailed` callbacks with `Reason = ScopeDisposed`, and logs a warning naming the leak.
- R7. `IBus`, `IQueue`, `ITimeJobManager<>`, `ICronJobManager<>`, `IJobScheduler` are registered scoped; their singleton cores carry no `IUnitOfWorkManager` dependency and take `IUnitOfWork?` as an argument. Framework-internal singletons (`HybridCache`, the three `DistributedLocks.Core` primitives) receive a unit-less `Bus` built over the core from their DI factories; `SubscribeExecutor`'s callback publish resolves `IBus` from the attempt scope or from a scope it opens; the Jobs seeders and the Jobs console demo's hosted service open a scope; the test bases (`MessagingIntegrationTestsBase`, `MessagingTestHarness`, the NATS/PostgreSQL and tenant-propagation suites) expose their publishers from a test-owned scope. Every test host in the affected suites enables `ValidateScopes`.
- R8. `TransactionEnlistment` lives in `Headless.UnitOfWork.Abstractions`; `MessageOptions.Enlistment`, `WithEnlistment(...)` on both message builders, `MessagingOptions.DefaultEnlistment`; `JobOptions.Enlistment`, `RecurringJobOptions.Enlistment`, `JobOptionsBuilder.WithEnlistment(...)`, the Jobs host default. `DeliveryMode` is `{ Durable = 0, Direct = 1 }`. The new header `headless-enlistment-requested` joins the framework-reserved header list.
- R9. Guarantee matrix (Messaging durable lane and Jobs writes alike):

| Enlistment | Active UoW, joinable compatible resource | No UoW, or UoW with no joinable resource | UoW with incompatible resource |
|---|---|---|---|
| `WhenAvailable` (default) | row in the transaction, dispatch after commit | autonomous durable write, relay/poller dispatches | throw |
| `Required` | same | throw | throw |
| `Never` | autonomous | autonomous | autonomous |

   `DeliveryMode.Direct` bypasses storage and implies `Never`; it still rejects `Delay` / `ScheduledAt`.
- R10. In-memory messaging storage joins a resource-less unit of work through the existing buffered-promotion seam (rows visible on complete, discarded on failure). Relational storages join only a relational resource on the same database.
- R11. The Headless save pipeline resolves the unit of work through the `DbContext` binding first and the scope manager second, adopting a foreign-scope unit for the save's duration (KD13). With a unit active on the same context it saves inside it and registers nothing; with no transaction it begins one, `Enlist(db, tx)`, saves, commits, `CompleteAsync`. With a **caller-owned transaction**, the pipeline requires a unit that is *bound to this context* (or whose `Resource` is the relational resource owning `Database.CurrentTransaction`) and `Active`; a missing unit **or a resource-less unit** throws in the pipeline's caller-owned branch with a message naming `unitOfWork.BeginAsync(db)`, before any domain-event or outbox dispatch. `OutboxIntegrationEventDispatcher` keeps its scoped `IBus` and mirrors the same check on `Current?.Resource`; `IHeadlessOutboxDispatcher`'s signature is unchanged.
- R12. `MessagingTestHarness.RunInUnitOfWorkAsync(Func<IServiceProvider, Task>)` creates a service scope, begins a resource-less unit of work, runs the action with the scope's provider, completes on success and abandons on exception. `harness.Publisher` / `harness.Queue` resolve from a harness-owned scope and carry no unit of work (autonomous durable), so `ValidateScopes` stays on.
- R13. `RunAsync(db, Func<IUnitOfWork, CancellationToken, Task>)` (EF) executes the block under `IExecutionStrategy` when the strategy retries, honoring `IsRetryPrevented` exactly as `ExecuteCoordinatedTransactionAsync` does today; `BeginAsync(db)` under a retrying strategy throws with EF's message plus the remedy naming `RunAsync`.
- R14. Public docs: `docs/llms/unit-of-work.md` replaces `commit-coordination.md`; `messaging.md`, `jobs.md`, `orm.md`, `testing.md`, `index.md`, `CONCEPTS.md`, every affected package README, the root READMEs, `eng/expected-packages.txt`, and the solution file describe only the contract that exists. The AsyncLocal learning records the resolution.
- R15. Old packages (`Headless.CommitCoordination.*`, `Headless.EntityFramework.CommitCoordination`) and every type named in the Migration table are gone; `rg` for the old names across `src`, `tests`, `docs`, `demo`, `benchmarks` returns nothing.

### Success Criteria

- The handler in Acceptance Example A compiles and its rollback test proves the outbox row and the job row are absent and nothing was dispatched.
- No `AsyncLocal` field exists under `src/Headless.UnitOfWork*`, `src/Headless.Messaging.Core`, or `src/Headless.Jobs.Core`; no comment in those packages mentions capturing before an await.
- Every message in the Verification Contract's catalogue is asserted by a unit test with `Contain(...)` on the substring that names the remedy.
- `make build`, `make format-check`, `make test-unit`, and `make quality-analyzers` are clean; the Docker suites named in the Verification Contract pass.

### Scope Boundaries

- No framework-opened unit of work (KD2). No mediator behavior, endpoint filter, or attribute.
- No savepoint-backed nested units (KD9, deferred); no `RequiresNew` / `Suppress` propagation options (deferred; the `UnitOfWorkOptions` record leaves room).
- No `InDoubt` outcome on the contract; the unknown-commit case is documented in the guarantee matrix, not modelled.
- No per-feature `ICoordinatedBus` facade; no persisted callback registry; no analyzer (nothing left to analyze).
- Messaging storage stays mandatory; the durability axis and the inbox tiers are unchanged.
- `JobsPostCommitSignalService`, the cron-cache inline invalidation, recurring-definition atomicity, and the Jobs retry guard mechanics are unchanged in behavior; only their capture and naming move.

### Acceptance Examples

**A. Transaction script, explicit window**

```csharp
public sealed class PlaceOrderHandler(IUnitOfWorkManager unitOfWork, AppDbContext db, IBus bus, ITimeJobManager jobs)
{
    public async Task<Result<OrderId>> Handle(PlaceOrder cmd, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([cmd.CustomerId], ct);   // outside any transaction
        if (customer is null) return Result.NotFound();

        await using var uow = await unitOfWork.BeginAsync(db, ct);           // the transaction starts here
        var order = Order.Place(customer, cmd.Lines);
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);                                        // inside; not committed
        await bus.PublishAsync(new OrderPlaced(order.Id), ct);                // outbox row, same transaction
        await jobs.AddAsync(SendReceipt.For(order.Id), ct);                   // job row, same transaction
        uow.OnCompleted(() => cache.RemoveAsync(customer.Id));
        await uow.CompleteAsync(ct);                                          // commit, then dispatch
        return Result.Ok(order.Id);
    }
}
```

Given a rollback (exception before `CompleteAsync`), the outbox row and the job row do not exist and nothing reached the transport or the scheduler. Given completion, both rows exist, `OrderPlaced` is dispatched after the commit, and the job is picked up by the immediate dispatcher.

**B. Consumer-declared requirement**

```csharp
messaging.ForMessage<OrderPlaced>().WithEnlistment(TransactionEnlistment.Required);
```

`bus.PublishAsync(new OrderPlaced(id), ct)` with no active unit of work throws before any effect: `"Publishing 'OrderPlaced' requires an active unit of work (TransactionEnlistment.Required) but none is active in this scope. Begin one with IUnitOfWorkManager.BeginAsync before publishing, or register the message with TransactionEnlistment.WhenAvailable."`

**C. Raw ADO, owned mode**

```csharp
await using var uow = await unitOfWork.BeginAsync(connection, ct);
await connection.ExecuteAsync("insert …", transaction: uow.Resource!.Transaction);
await bus.PublishAsync(new StockAdjusted(sku), ct);
await uow.CompleteAsync(ct);
```

**D. Observed mode (advanced)**

```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);
await using var uow = unitOfWork.Enlist(db, tx);
// … work, publishes enlist on uow …
await tx.CommitAsync(ct);
await uow.CompleteAsync(ct);   // drains; a dispose without this is treated as rolled back and logs the forgotten completion
```

**E. Leak detection**

A unit of work begun in a request and never completed or disposed: when the request scope disposes, the manager rolls it back, runs `OnFailed` with `Reason = ScopeDisposed`, and logs `"A unit of work begun with BeginAsync was still active when its service scope was disposed; it was rolled back. Complete or dispose every unit of work before the scope ends."`

**F. Test harness**

```csharp
await harness.RunInUnitOfWorkAsync(async sp =>
{
    var bus = sp.GetRequiredService<IBus>();
    await bus.PublishAsync(new Ping(), AbortToken);
    harness.Published.Should().BeEmpty();   // nothing before completion
});
harness.Published.Should().ContainSingle();
```

## Planning Contract

### Key Technical Decisions

- KTD1. **Engine reuse.** `CommitCoordinator.cs` (atomic terminal claim, ordered drain, fault aggregation, scope-state disposal, deregistration handles) moves to `Headless.UnitOfWork` as the internal `UnitOfWork` engine with two additions: the `OnFailed` list drained (log-and-continue) on rollback/abandon, and the child-registration transfer. `CommitScopeStack`, `CommitScopeFactory`, and `CommitScope` are deleted; their responsibilities are the manager's field and the handle's dispose. An `OnCompleted` fault after a successful commit propagates from `CompleteAsync` but leaves `State == Completed`: the data is durable and the exception says so, so a caller cannot mistake it for a rollback (ABP marks the unit `Failed` in that case, which invites a retry that double-applies).
- KTD2. **Manager state.** `UnitOfWorkManager` holds `UnitOfWork? _root`, a `Beginning` latch, and a child depth counter under a `Lock`. `BeginAsync` claims the slot **synchronously** (state `Beginning`) before its first `await`, releases it if the resource begin faults, and only then publishes `_root`; a concurrent `BeginAsync`/`Enlist` that observes `Beginning` throws the catalogued message. With `_root` active: same resource → `ChildUnitOfWork` view (registrations tagged with the child's generation so an abandoned child's registrations can be dropped); resource-less root + resource-bearing begin → independent nested unit; resource-bearing root + different resource → throw. `Current` returns the innermost. The manager implements `IAsyncDisposable` for R6.
- KTD3. **Resource contract.** `IUnitOfWorkResource { ValueTask CommitAsync(ct); ValueTask RollbackAsync(ct); }`; `IRelationalUnitOfWorkResource : IUnitOfWorkResource { DbConnection Connection; DbTransaction Transaction; }` (non-null while active; participants snapshot and validate identity as `JobsManager` does today). Owned vs observed is a flag on the resource instance, never a second handle type: an observed resource's `CommitAsync`/`RollbackAsync` are no-ops.
- KTD4. **EF provider.** `BeginAsync(db, isolation = ReadCommitted, ct)`: checks the manager slot first, then throws if `db.Database.CurrentTransaction is not null`; checks `CreateExecutionStrategy().RetriesOnFailure` eagerly and throws the `RunAsync` remedy; begins the transaction; enlists `EfUnitOfWorkResource(owned: true)`; records the `DbContext → IUnitOfWork` binding (KD13). `Enlist(db, IDbContextTransaction)`: enlists `owned: false` and records the binding. `RunAsync`: `CreateExecutionStrategy().ExecuteAsync` around begin → block → complete, rethrowing outside the strategy when `CommitStarted || IsRetryPrevented` (today's filter). `HeadlessSaveChangesPipeline` reads the binding through an internal accessor on the provider.
- KTD5. **ADO providers.** Same members on `NpgsqlConnection` / `SqlConnection`; the forgotten-completion warning fires only when the unit was neither completed nor rolled back (`RollbackAsync`) and the transaction has finished. `CoordinatedTransactionRunner` becomes the shared `RunAsync` body.
- KTD6. **Messaging.** `MessagePublisher.PublishAsync(lane, content, options, IUnitOfWork? unitOfWork, ct)`; `DeliveryDecisionResolver` takes `TransactionEnlistment` + `DeliveryMode` + `DeliveryCoordination` and yields `DeliveryPath { Direct, DurableStandalone, DurableEnlisted }`; `IDeliveryCoordinationResolver.Resolve(IUnitOfWork)` reads `Resource`; `ICoordinatedMessageStore` keys on `IUnitOfWork`; the `Coordinated` startup gate is reduced to the inbox-tier check (the manager always exists). `SubscribeExecutor` publishes callback responses through `services.GetRequiredService<IBus>()` on the attempt scope when `services` is non-null (`ISubscribeExecutor.cs:730` today resolves from the root) and, on the `services: null` branch (`:264`, non-transactional tier), inside a scope it opens with `provider.CreateAsyncScope()` — no unit of work, autonomous durable. Header values become `Durable | Direct` (resolved) and `WhenAvailable | Required | Never` (requested enlistment, new reserved header `headless-enlistment-requested` in `IMessagePublishRequestFactory`); the dashboard DTOs and the `MessageDetailDialog.vue` type literal render the new strings.
- KTD7. **Jobs.** `JobsManager` core keeps `_TryCaptureCoordinatedContext` but takes `IUnitOfWork?`; the drift re-read at `JobsManager.CommitCoordination.cs:87-93` becomes a `unitOfWork.State == Active` re-validation before the write. The scoped facades resolve `IUnitOfWorkManager.Current` at each call. `RequireAtomicEnlistment` → `Enlistment` on options, builder (`WithEnlistment`), policies (`JobSchedulingPolicies` composes by *strictest wins*: `Required` > `WhenAvailable` > `Never` — an explicit `Never` on a call is honored only when no tier says `Required`), entities (transient, EF-ignored, `[JsonIgnore]`), `JobAtomicity` (tree walk unchanged), scheduler copies. `JobsInitializationHostedService` runs each seeder inside `serviceProvider.CreateAsyncScope()`.
- KTD8. **Save pipeline.** `HeadlessSaveChangesPipeline` resolves the unit through the EF provider's context binding, falling back to the scoped `IUnitOfWorkManager.Current`; a bound unit owned by another scope's manager is adopted for the save's duration (KD13). Caller-owned transaction with a unit → save inside, register nothing. Caller-owned transaction with no unit → the outbox dispatcher's fail-loud, message updated. No transaction → begin, `Enlist(db, tx)`, save, commit, `CompleteAsync`. The `IHeadlessTransactionCoordinator` seam and its null default are deleted. `HeadlessDbContextTransactionExtensions.ExecuteTransactionAsync` delegates to the EF provider's `RunAsync`. `IHeadlessOutboxDispatcher` is unchanged; `OutboxIntegrationEventDispatcher` injects `IUnitOfWorkManager` in place of `ICurrentCommitCoordinator`.
- KTD9. **Registration.** `services.AddUnitOfWork()` (idempotent; `TryAddScoped<IUnitOfWorkManager, UnitOfWorkManager>`). `AddHeadlessMessaging`, `AddHeadlessJobs`, `AddHeadlessDbContextServices`, and the three provider setups call it. `IBus`/`IQueue`: `TryAddScoped<IBus>(sp => new Bus(sp.GetRequiredService<MessagePublisher>(), sp.GetRequiredService<IUnitOfWorkManager>()))`; the direct-construction `Bus` ctor (transport-only, `Direct`) passes `unitOfWork: null`. The `HybridCache` and `DistributedLocks.Core` factories pass `new Bus(sp.GetRequiredService<MessagePublisher>())` (or `null` where the bus is optional and messaging is absent) so the public `IBus` constructor parameters and their substituting unit tests are untouched.
- KTD10. **Test harness scoping.** `MessagingTestHarness.RunInUnitOfWorkAsync` creates the scope itself (R12). `harness.Publisher` / `harness.Queue` open a harness-owned scope (disposed with the harness) and carry no unit of work — matching Wolverine's "harmless fresh context" fallback; the harness builds its host with `ValidateScopes = true` so accidental captive resolution fails in tests. `MessagingIntegrationTestsBase.Publisher` / `QueuePublisher` / `Bus` resolve from a test-owned scope the same way.

### High-Level Technical Design

```
Headless.UnitOfWork.Abstractions       IUnitOfWorkManager · IUnitOfWork · IUnitOfWorkResource · IRelationalUnitOfWorkResource
                                       UnitOfWorkState · UnitOfWorkFailure · UnitOfWorkOptions · TransactionEnlistment   (zero deps)
Headless.UnitOfWork                    UnitOfWorkManager (scoped) ── Current ──► IUnitOfWork (root / child / nested)
  ├─ UnitOfWork engine (internal)                 │ BeginAsync(options)               ├─ Resource : IUnitOfWorkResource?
  ├─ ChildUnitOfWork (internal)                   │ Enlist(resource)                  ├─ OnCompleted / OnFailed / GetOrAdd
  └─ AddUnitOfWork()                              └─ (provider extensions)            ├─ CompleteAsync ──► resource.CommitAsync ──► drain
                                                                                      ├─ RollbackAsync ──► resource.RollbackAsync ──► OnFailed
                                                                                      └─ Dispose (no Complete) ──► same as RollbackAsync

Headless.UnitOfWork.EntityFramework    BeginAsync(db) · Enlist(db, tx) · RunAsync(db, block) · EfUnitOfWorkResource · DbContext→IUnitOfWork binding
Headless.UnitOfWork.PostgreSql         BeginAsync(NpgsqlConnection) · Enlist(conn, tx) · RunAsync
Headless.UnitOfWork.SqlServer          BeginAsync(SqlConnection) · Enlist(conn, tx) · RunAsync

Participants (read the unit from the scoped manager or the context binding, never ambient):
  Messaging: scoped Bus/Queue ──► MessagePublisher(core, IUnitOfWork?) ──► OutboxMessageWriter / storages
             HybridCache, DistributedLock* (singletons) ──► MessagePublisher(core, unitOfWork: null, Direct)
             SubscribeExecutor callback ──► attempt-scope IBus (joins the inbox unit)
  Jobs:      scoped managers  ──► JobsManager(core, IUnitOfWork?)      ──► ICoordinatedJobWriter
  EF:        HeadlessSaveChangesPipeline ──► binding ?? manager.Current (foreign-scope unit adopted for the save) ──► Enlist(db, tx) ──► CompleteAsync after its own commit
             ──► IHeadlessOutboxDispatcher (scoped IBus; manager.Current is the adopted/bound unit)
```

Lifecycle of `IUnitOfWork`: `Beginning` (manager-internal) → `Active` → (`CompleteAsync`) `Completed` | (`RollbackAsync`, dispose, child abandon, scope dispose, commit fault) `Failed`. `Failed` carries `UnitOfWorkFailure { Reason: RolledBack | Abandoned | Faulted | ScopeDisposed | ChildAbandoned; Exception? }`. A commit fault transitions to `Failed` before the exception propagates, so a second `CompleteAsync` throws the "already failed" message rather than re-committing (NServiceBus's missing-failed-state gap, avoided). An `OnCompleted` fault after a successful commit leaves `Completed`.

### Assumptions

- No persisted column stores `DeliveryMode` numerically (headers are `"G"` strings parsed with `TryParse` + `IsDefined`; `RequireAtomicEnlistment` is EF-ignored and `[JsonIgnore]`). Verified in review; rows written by #895 builds with a `"Coordinated"` header parse to `null` and render as "Not recorded".
- Root-provider resolutions of the five services in this repository are exactly: `HybridCache` (`Caching.Hybrid/Setup.cs:199`), `DistributedLock` / `DistributedReadWriteLock` / `DistributedSemaphoreProvider` (`DistributedLocks.Core/Setup.cs:139,187,219`), `SubscribeExecutor` callback publish (`ISubscribeExecutor.cs:730`, serving both the transactional branch `:255` and the `services: null` branch `:264`), the Jobs seeders (`JobsInitializationHostedService.cs:125,130`), the harness (`MessagingTestHarness.cs:562,565`), `demo/Headless.Jobs.Console.Demo/Jobs.cs:23`, and the test bases `tests/Headless.Messaging.Core.Tests.Harness/MessagingIntegrationTestsBase.cs:65,68,77`, `tests/Headless.Messaging.NatsPostgreSql.Tests.Integration/NatsPostgreSqlMessagingIntegrationTests.cs:199,219`, `tests/Headless.Messaging.Testing.Tests.Unit/MultiTenancy/TenantPropagationE2ETests.cs:431` (19 root resolutions under `tests` in total). Each has a unit below; `ValidateScopes` on the test hosts plus the DoD sweep is the regression gate.
- Downstream consumers are greenfield with this repository; a singleton that captured `IBus` is a bug the scope validator will now name.

### Sequencing

U1 → U2 ∥ U3 → U4 → U5 ∥ U6 → U7 → U8. The old packages stay in the solution until U7 so every unit builds green; U7 deletes them and the migration table becomes final.

### System-Wide Impact

- Lifetimes: five public services move from singleton to scoped (KD5).
- Packages: `Headless.CommitCoordination.{Abstractions,Core,EntityFramework,PostgreSql,SqlServer}` and `Headless.EntityFramework.CommitCoordination` are removed; `Headless.UnitOfWork{,.EntityFramework,.PostgreSql,.SqlServer}` are added; `eng/expected-packages.txt` and `headless-framework.slnx` change.
- Startup: the EF commit-interceptor startup gate no longer runs; `CommitProbeMode` and `CommitInterceptorProbeOptions` are gone.
- Telemetry/dashboards: delivery-mode tag values change (`Coordinated` disappears; enlistment gains its own tag); the Vue components render strings and need no logic change.
- Learnings: `docs/solutions/logic-errors/asynclocal-ambient-scope-stranded-across-await.md` gets a "Resolution" section pointing at the scoped design.

### Risks

- **Captive-dependency surprises downstream.** A singleton holding `IBus` breaks at startup with MS-DI's scope validation (or silently gets a root bus without validation). Mitigation: R7 docs, a `docs/llms/unit-of-work.md` Agent Rule, and the harness enabling `ValidateScopes`.
- **Hosts where a scope is not an operation** (Blazor Server circuits, long-lived hosted services). Mitigation: R6 leak detection, the "concurrent begin is a join" rule documented, and the Agent Rule "one operation, one scope; create a child scope for background work".
- **Retrying execution strategies.** `BeginAsync(db)` is incompatible by EF's rule. Mitigation: the wrapped message names `RunAsync` (R13).
- **Two DbContexts on one request.** Throws on the second `BeginAsync` (KD9). Mitigation: message names the child-scope remedy; multi-resource support is a later additive option.
- **Size.** Thirty-eight source directories and forty-five test directories reference the old contract. Mitigation: unit boundaries below keep the branch green; the wiring map (`scratchpad/wiring-map.md`) enumerates every site.

## Implementation Units

### U1. `Headless.UnitOfWork.Abstractions`, `Headless.UnitOfWork`, and the conformance harness

- **Files:** new `src/Headless.UnitOfWork.Abstractions/` (`IUnitOfWorkManager.cs`, `IUnitOfWork.cs`, `IUnitOfWorkResource.cs`, `IRelationalUnitOfWorkResource.cs`, `UnitOfWorkState.cs`, `UnitOfWorkFailure.cs`, `UnitOfWorkOptions.cs`, `TransactionEnlistment.cs`, `README.md`); new `src/Headless.UnitOfWork/` (`UnitOfWorkManager.cs`, `Internal/UnitOfWork.cs` (engine, ported from `src/Headless.CommitCoordination.Core/CommitCoordinator.cs`), `Internal/ChildUnitOfWork.cs`, `Internal/BackgroundFault.cs`, `Setup.cs`, `README.md`); new `tests/Headless.UnitOfWork.Tests.Harness/` (ported from `tests/Headless.CommitCoordination.Tests.Harness/`) and `tests/Headless.UnitOfWork.Tests.Unit/` (ported from `tests/Headless.CommitCoordination.Core.Tests.Unit/` and `tests/Headless.CommitCoordination.Conformance.Tests.Unit/`); `headless-framework.slnx`, `eng/expected-packages.txt`.
- **Behavior:** R1–R6, KD8, KD9 (join + independent nested unit), KTD1–KTD3, KTD9 (the `AddUnitOfWork()` call only).
- **Verification:** the 13 ambient-independent conformance scenarios ported as-is; the 5 ambient-specific ones replaced by manager-scope scenarios; new: `OnFailed` runs on `RollbackAsync`, abandon, and scope dispose; child transfer on complete; child abandon aborts root; root refuses to complete with an active child; resource-less root + resource-bearing begin runs independently; second resource throws; concurrent begin/begin race (two tasks, 200 iterations) throws the catalogued message and never yields two roots; every catalogue message asserted; a 200-iteration two-thread complete/dispose race; a `ValidateScopes` host resolving `IUnitOfWorkManager` from root throws.

### U2. `Headless.UnitOfWork.EntityFramework`

- **Files:** new `src/Headless.UnitOfWork.EntityFramework/` (`Setup.cs`, `UnitOfWorkManagerEntityFrameworkExtensions.cs` in `Headless.UnitOfWork`, `EfUnitOfWorkResource.cs`, `Internal/DbContextUnitOfWorkBinding.cs`, `README.md`; references `Headless.UnitOfWork` + `Microsoft.EntityFrameworkCore.Relational` only — **never** `Headless.EntityFramework`); tests `tests/Headless.UnitOfWork.EntityFramework.Tests.Unit/` (ported from `tests/Headless.CommitCoordination.EntityFramework.Tests.Unit/`, minus interceptor/gate/probe).
- **Behavior:** KD13, KTD4, R13. No interceptor, options configuration, startup gate, or probe options.
- **Verification:** owned begin/complete/rollback against SQLite in-memory and the EF conformance fixture; `CurrentTransaction` pre-existing throws; retrying-strategy message (eager `RetriesOnFailure` check); `RunAsync` replay filter; observed mode: `RollbackAsync` then dispose logs nothing, dispose without either after a finished transaction logs the warning; the binding resolves the unit for a context whose scope has no manager.

### U3. `Headless.UnitOfWork.PostgreSql` and `.SqlServer`

- **Files:** new provider packages (from the two `CommitCoordination.*` packages: `Setup.cs`, `UnitOfWorkManager<Provider>Extensions.cs`, `<Provider>UnitOfWorkResource.cs`, `README.md`); shared `RunAsync` body moves into `Headless.UnitOfWork` as an internal runner; tests unit + integration renamed.
- **Behavior:** KTD5.
- **Verification:** existing 5 + 5 integration scenarios on Docker plus the owned-mode commit and the forgotten-completion warning.

### U4. Headless save pipeline on the manager

- **Files:** `src/Headless.EntityFramework/Headless.EntityFramework.csproj` (reference `Headless.UnitOfWork.EntityFramework`), `Contexts/Runtime/HeadlessSaveChangesPipeline.cs`, `SetupEntityFramework.cs`, `Extensions/HeadlessDbContextTransactionExtensions.cs`, `Contexts/HeadlessDbContextServices.cs`; delete `Contexts/Runtime/IHeadlessTransactionCoordinator.cs`; `src/Headless.EntityFramework.Messaging/Headless.EntityFramework.Messaging.csproj` (swap the adapter reference for the provider), `Setup.cs` (drop `AddCommitCoordination()`), `OutboxIntegrationEventDispatcher.cs` (`IUnitOfWorkManager`, new message); delete `src/Headless.EntityFramework.CommitCoordination/` and `tests/Headless.EntityFramework.CommitCoordination.Tests.{Unit,Integration}/`; `tests/Headless.Identity.Storage.EntityFramework.Tests.Integration/` (csproj reference and `Fixture/IdentityTestFixture.cs:34`, `HeadlessIdentityDbContextCoordinatedTransactionTests.cs` → `RunAsync`); `tests/Headless.EntityFramework.Messaging.Tests.Unit/OutboxIntegrationEventDispatcherTests.cs`; `tests/Headless.EntityFramework.Tests.Integration/` (new save-pipeline scenarios below).
- **Behavior:** KD13, KTD8, R11.
- **Verification:** new Docker scenarios in `Headless.EntityFramework.Tests.Integration`: rollback discards the outbox row; caller-owned transaction inside a unit enlists; caller-owned transaction without one throws the new message; **a resource-less root plus a caller-owned transaction plus an integration event throws** (no autonomous write); `IsRetryPrevented` routes a coordinated write out of the retry filter; a context from `IDbContextFactory<T>` inside `BeginAsync(db)` saves and dispatches atomically **and** a domain-event handler in that context's scope publishing via `IBus` has its row roll back with the unit; a handler calling `SaveChangesAsync` on the same context re-enters the pipeline without error. `EntityFramework.Messaging.Tests.Integration` atomicity; Identity integration suite green.

### U5. Messaging on the scoped manager and `TransactionEnlistment`

- **Files:** `Messaging.Abstractions` (csproj → `Headless.UnitOfWork.Abstractions`; `DeliveryMode.cs`, `MessageOptions.cs`, `Headers.cs`), `Messaging.Core` (`Setup.cs`, `Internal/Bus.cs`, `Internal/Queue.cs`, `Internal/MessagePublisher.cs`, `Internal/DeliveryDecisionResolver.cs`, `Internal/DeliveryCoordination.cs`, `Internal/IDeliveryCoordinationResolver.cs`, `Internal/ICoordinatedMessageStore.cs`, `Internal/OutboxMessageWriter.cs`, `Internal/DeliveryMetadata.cs`, `Internal/DeliveryModeTagEnricher.cs`, `Internal/IBootstrapper.Default.cs`, `Internal/ISubscribeExecutor.cs` (callback publish via the attempt scope), `Internal/IMessagePublishRequestFactory.cs` (reserved header), `Configuration/MessagingCapabilityModel.cs`, `Configuration/MessagingOptions.cs`, `Registration/MessageBuilder.cs`, `Registration/MessageRegistration.cs`, `PublishContext.cs`, `Transactions/MessageOutboxBuffer.cs`, delete `Transactions/MessagingNullCommitCoordinator.cs`, telemetry/tags/metrics/monitoring), the three storages, the two EF storage inbox runners (`RollbackAsync` on the rollback path), `Messaging.Dashboard` endpoint DTOs and `wwwroot/src/components/MessageDetailDialog.vue`, `Caching.Hybrid/Setup.cs` + `HybridCache.cs` and `DistributedLocks.Core/Setup.cs` + the three primitives (inject `MessagePublisher`, publish `Direct`), `Messaging.RabbitMq` (no change; false positive); tests incl. `Messaging.Abstractions.Tests.Unit/PublishOptionsTests.cs` (numeric assertions), `Messaging.Dashboard.Tests.Unit`, `Caching.Hybrid.Tests.Unit`, `DistributedLocks.Composition.Tests.Unit`.
- **Behavior:** KD5–KD7, KTD6, R7–R10.
- **Verification:** delivery matrix theory rewritten for (`Enlistment` × `DeliveryMode` × coordination state); precedence per call > per type > host; `Required` message; capability gate; in-memory promote/discard; storage resolvers against PostgreSQL and SQL Server (Docker); inbox runner conformance on both databases plus two new cases: a consumer callback response rolls back with the handler on the transactional tier, and a callback response on the non-transactional tier succeeds under a `ValidateScopes` host; dashboard DTO strings; `ValidateScopes` on every messaging, caching, and locks test host.

### U6. Jobs on the scoped manager and `TransactionEnlistment`

- **Files:** `Jobs.Abstractions` (csproj → `Headless.UnitOfWork.Abstractions`; `Models/JobOptions.cs`, `Models/RecurringJobOptions.cs`, `JobOptionsBuilder.cs`, `Entities/TimeJobEntity.cs`, `Entities/CronJobEntity.cs`, `Interfaces/ICoordinatedJobWriter.cs`), `Jobs.Core` (`DependencyInjection/SetupJobs.cs`, `Managers/JobsManager*.cs`, `JobAtomicity.cs`, `JobScheduler*.cs`, `JobSchedulingPolicies.cs`, `BackgroundServices/JobsInitializationHostedService.cs` (scoped seeders), delete `Transactions/JobsNullCommitCoordinator.cs`), `Jobs.EntityFramework*` (`Infrastructure/JobsEFCorePersistenceProvider*.cs`, configurations, provider setups).
- **Behavior:** KD5–KD7, KTD7.
- **Verification:** `Jobs.Composition.Tests.Unit` routing and policy composition (strictest-wins), required-without-uow message, aligned matrix; a `ValidateScopes` host running the seeders; `Jobs.EntityFramework.Tests.Harness` on PostgreSQL and SQL Server (Docker); keyed savepoint requirement unchanged.

### U7. Test support, internal consumers, and deletion of the old packages

- **Files:** `Messaging.Testing/MessagingTestHarness.cs` (`RunInUnitOfWorkAsync`, scoped `Publisher`/`Queue`, `ValidateScopes`), `tests/Headless.Messaging.Core.Tests.Harness/MessagingIntegrationTestsBase.cs` (scoped `Publisher`/`QueuePublisher`/`Bus`), `tests/Headless.Messaging.NatsPostgreSql.Tests.Integration/NatsPostgreSqlMessagingIntegrationTests.cs`, `tests/Headless.Messaging.Testing.Tests.Unit/MultiTenancy/TenantPropagationE2ETests.cs`, `demo/**` (the 9 wiring-map files plus `demo/Headless.Jobs.Console.Demo/Jobs.cs` hosted service → scope), `benchmarks/Headless.Messaging.Benchmarks/Scenarios/PublishDispatchBenchmarks.cs`; delete `src/Headless.CommitCoordination.*` and their tests; `headless-framework.slnx`, `eng/expected-packages.txt`; every `packages.lock.json` touched by the reference changes. `Headless.Testing.AspNetCore` needs no change.
- **Behavior:** R12, R15.
- **Verification:** `Messaging.Testing.Tests.Unit` (harness semantics incl. multi-tenancy, `Publisher` under `ValidateScopes`); `rg` for every name in the Migration table returns nothing under `src`, `tests`, `demo`, `benchmarks`; the two DoD sweeps below return nothing; `make build` on the full solution.

### U8. Docs

- **Files:** new `docs/llms/unit-of-work.md` (Orientation, Agent Rules, Core Concepts, Choosing a Provider, four package contracts); delete `docs/llms/commit-coordination.md`; `docs/llms/{messaging,jobs,orm,testing,caching,distributed-locks,identity,index}.md`; `CONCEPTS.md`; package READMEs listed in the wiring map §10; `README.md`, `README.ar.md`; `docs/solutions/logic-errors/asynclocal-ambient-scope-stranded-across-await.md` (Resolution section); `docs/solutions/architecture-patterns/coordination-domains-boundary.md`; `CLAUDE.md` learnings entry.
- **Behavior:** R14; AUTHORING.md drift checks 1–5.
- **Verification:** every code sample in the new docs compiles against the public API (spot-built in a scratch project); `rg` for old names under `docs` returns only the plan history and the learning's "before" section.

## Verification Contract

| Requirement | Evidence |
|---|---|
| R1–R6, KD8, KD9 | `Headless.UnitOfWork.Tests.Unit` + conformance harness (U1) |
| R7, KTD9 | `ValidateScopes` hosts in Messaging/Jobs unit suites; Setup tests asserting lifetimes |
| R8, R9 | `DeliveryDecisionResolverTests` matrix theory; `JobSchedulingPoliciesTests`; `MessagePublisherDeliveryTests` precedence |
| R10 | `InMemoryDataStorageCoordinationTests` |
| R11 | `EntityFramework.Messaging.Tests.Integration`, `EntityFramework.Tests.Integration` (Docker) |
| R12 | `Messaging.Testing.Tests.Unit` |
| R13 | `UnitOfWork.EntityFramework.Tests.Unit` retry cases |
| R14 | AUTHORING drift checks; sample compile |
| R15 | `rg` sweep; `make build` |
| Gates | `make format-check`, `make build`, `make test-unit`, `make quality-analyzers`; Docker: UnitOfWork PostgreSQL/SqlServer, EF integration, Messaging Storage PostgreSQL/SqlServer, EF Messaging, Jobs EF PostgreSQL/SqlServer |

Message catalogue (each asserted by a test on the underlined remedy substring):

| Condition | Message |
|---|---|
| `BeginAsync(db)` with `CurrentTransaction != null` | `The DbContext already has an active transaction. Begin the unit of work before beginning the transaction, or call IUnitOfWorkManager.Enlist(db, transaction) for a transaction you commit yourself.` |
| `BeginAsync(db)` under a retrying strategy | EF's text + ` Use IUnitOfWorkManager.RunAsync(db, …) to run the unit of work as a retriable block.` |
| `CompleteAsync` after `Completed` | `The unit of work has already completed. Begin a new unit of work for further work.` |
| `CompleteAsync` after `Failed` | `The unit of work has already failed ({Reason}) and cannot be completed. Begin a new unit of work.` |
| any member after dispose | `ObjectDisposedException("UnitOfWork")` |
| `OnCompleted`/`OnFailed`/`GetOrAdd` after terminal | `The unit of work is {State}; registrations are accepted only while it is Active.` |
| second `BeginAsync` with a different resource | `A unit of work is already active on another resource in this scope. Complete it first, or run the second operation in its own service scope (IServiceScopeFactory.CreateScope()).` |
| `BeginAsync`/`Enlist` while another begin is in flight in the same scope, or `Adopt` while the scope holds a different active unit | `Another unit of work is being begun concurrently in this scope. Await the first BeginAsync before beginning again, or run parallel work in separate service scopes.` |
| caller-owned transaction saved with no unit bound to the context, or with a resource-less unit | `SaveChanges ran inside a caller-owned transaction that no unit of work owns, so integration events and jobs would dispatch non-atomically. Begin the unit of work on this context (IUnitOfWorkManager.BeginAsync(db)) before beginning the transaction, or enlist the transaction with Enlist(db, transaction).` |
| `CompleteAsync`/`RollbackAsync`/registration on a unit whose scope manager was disposed | `ObjectDisposedException("UnitOfWorkManager")` |
| root `CompleteAsync` after a child was abandoned | `A nested unit of work was disposed without completing, so the root cannot complete; the transaction is rolled back.` |
| root `CompleteAsync` while a child is still active | `A nested unit of work begun in this scope is still active. Complete or dispose it before completing the root.` |
| resource refuses to begin a transaction | propagated as-is from the provider — never downgraded to a warning (ABP logs `Current database does not support transactions…` and continues non-transactionally; this plan does not) |
| `Required` with no unit of work (Messaging) | Acceptance Example B |
| `Required` with no unit of work (Jobs) | `Scheduling '{Function}' requires an active unit of work (TransactionEnlistment.Required) but none is active in this scope. Begin one with IUnitOfWorkManager.BeginAsync, or register the function with TransactionEnlistment.WhenAvailable.` |
| incompatible resource | `The active unit of work's transaction belongs to another database ({Mismatch}), so '{Participant}' cannot enlist. Use the same database, or TransactionEnlistment.Never for this call.` |
| scope disposed with an active unit of work | Acceptance Example E (warning) |
| observed mode disposed un-completed and un-rolled-back after the transaction finished | `A unit of work enlisted with Enlist(...) was disposed without CompleteAsync or RollbackAsync after its transaction completed; the after-commit work was discarded and durable rows will be recovered by the relay.` (warning) |

## Definition of Done

- Every unit's verification ran and is reported with counts; Docker suites named above ran on the final head.
- `rg -n "CommitCoordination|ICommitCoordinator|ICommitScope|ICurrentCommitCoordinator|IRelationalCommitContext|CommitRetryGuard|RequireAtomicEnlistment|DeliveryMode\.Coordinated|'Coordinated'|ExecuteCoordinatedTransactionAsync|RunCoordinatedAsync"` across `src tests docs demo benchmarks README* CONCEPTS.md` returns only this plan and the learning's "before" section.
- `rg` over `src demo` for `IHostedService`/`BackgroundService` constructors injecting `IBus`, `IQueue`, `ITimeJobManager`, `ICronJobManager`, or `IJobScheduler` returns nothing.
- `rg -nP "(?<![Ss]cope\.)\bServiceProvider\.GetRequiredService<(IBus|IQueue|IJobScheduler|ITimeJobManager|ICronJobManager)"` over `src tests demo benchmarks` returns nothing (every publisher/manager is resolved from a scope; line-wrapped `scope\n.ServiceProvider` chains are reformatted onto one line so the sweep is exact).
- No `AsyncLocal` under `src/Headless.UnitOfWork*`, `src/Headless.Messaging.Core`, `src/Headless.Jobs.Core`.
- PR #897 title and body rewritten for the new scope; #895's description gains a note that its `DeliveryMode.Coordinated` and Jobs `RequireAtomicEnlistment` surface is superseded in #897.

## Migration

| Before | After |
| --- | --- |
| `Headless.CommitCoordination.Abstractions` + `.Core` | `Headless.UnitOfWork.Abstractions` + `Headless.UnitOfWork` |
| `Headless.CommitCoordination.EntityFramework` + `Headless.EntityFramework.CommitCoordination` | `Headless.UnitOfWork.EntityFramework` (referenced by `Headless.EntityFramework`) |
| `Headless.CommitCoordination.PostgreSql` / `.SqlServer` | `Headless.UnitOfWork.PostgreSql` / `.SqlServer` |
| `ICurrentCommitCoordinator.Current` | `IUnitOfWorkManager.Current` (scoped) |
| `ICommitScopeFactory.Open(relational)` | `IUnitOfWorkManager.BeginAsync()` / `Enlist(resource)` |
| `ICommitCoordinator.OnCommit(work)` | `IUnitOfWork.OnCompleted(work)` |
| (rollback via disposable scope state) | `IUnitOfWork.OnFailed(failure => …)` |
| `ICommitCoordinator.Relational` | `IUnitOfWork.Resource as IRelationalUnitOfWorkResource` |
| `ICommitScope.SignalAsync(Committed)` | `IUnitOfWork.CompleteAsync(ct)` |
| `ICommitScope.SignalAsync(RolledBack)` / un-signalled dispose | `IUnitOfWork.RollbackAsync()` / dispose without `CompleteAsync` |
| `CommitRetryGuard` via `GetOrAdd` | `IUnitOfWork.PreventRetry()` / `IsRetryPrevented` |
| `db.ExecuteCoordinatedTransactionAsync(op, services)` | `unitOfWork.RunAsync(db, op)` or `await using var uow = await unitOfWork.BeginAsync(db)` |
| `db.Database.EnlistCommitCoordination(tx, services)` | `unitOfWork.Enlist(db, tx)` |
| `connection.ExecuteCoordinatedTransactionAsync(op, services)` / `EnlistCommitCoordination(tx, services)` | `unitOfWork.RunAsync(connection, op)` / `BeginAsync(connection)` / `Enlist(connection, tx)` |
| `AddEntityFrameworkCommitCoordination<TContext>()`, `AddCommitCoordination()` | `AddUnitOfWork()` (called by the framework setups); `AddEntityFrameworkUnitOfWork()` for the EF provider |
| `DeliveryMode.Coordinated` | `TransactionEnlistment.Required` (per call, `WithEnlistment` per type, `DefaultEnlistment` host) |
| `DeliveryMode.Durable` / `.Direct` | unchanged names; `Direct` now implies `Never` |
| `RequireAtomicEnlistment` (bool, 6 types) | `Enlistment: TransactionEnlistment`; `JobOptionsBuilder.WithEnlistment(...)` |
| `harness.RunCoordinatedAsync(() => …)` | `harness.RunInUnitOfWorkAsync(sp => …)`; `harness.Publisher` / `Queue` carry no unit of work |
| `IBus`, `IQueue`, `ITimeJobManager<>`, `ICronJobManager<>`, `IJobScheduler` singleton | scoped; a singleton or hosted service that needs one creates a scope |

## Appendix

### Sources

- Research reports (this session's scratchpad): `research-A-dotnet-messaging.md`, `research-B-dotnet-platform.md`, `research-C-other-ecosystems.md`, `scoped-A-masstransit-wolverine.md`, `scoped-B-nsb-marten-ef.md`, `scoped-C-abp-mikroorm.md`, `wiring-map.md`.
- Precedent for the scoped holder: MassTransit `src/MassTransit/DependencyInjection/DependencyInjection/ScopedConsumeContextProvider.cs` (plain field, push/pop, `Interlocked.CompareExchange` pop guard); Wolverine `src/Wolverine/Runtime/ScopedMessageContextHolder.cs` (GH-2583, GH-3001); NServiceBus `TransactionalSessionBase.cs:234-237`.
- Precedent for the guard order and messages: NServiceBus `TransactionalSessionBase.ThrowIfInvalidState` (disposed → committed → not-opened); EF `CoreStrings.ExecutionStrategyExistingTransaction`.
- Precedent for the nesting rule: Rails `activerecord/lib/active_record/connection_adapters/abstract/transaction.rb` (`append_callbacks`), Laravel `DatabaseTransactionsManager::removeCommittedTransactionsThatAreChildrenOf`, Django `transaction.on_commit` savepoint samples.
- Precedent for the failure hook: `System.Transactions.Transaction.TransactionCompleted`, EF `IDbTransactionInterceptor.TransactionRolledBack`, ABP `IUnitOfWork.Failed`, Laravel `Queue::enqueueUsing` (`addCallbackForRollback` releasing `ShouldBeUnique` locks).
