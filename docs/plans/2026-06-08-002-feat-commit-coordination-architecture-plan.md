---
date: 2026-06-08
status: active
topic: commit-coordination-architecture
supersedes: docs/plans/2026-06-08-001-feat-ambient-transactions-extraction-plan.md
origin: docs/ideation/ (run d72ccd7e) + 9-reviewer review of PR #424
tracks: "#265"
unblocks: "#270"
---

# Commit Coordination — Replacement Architecture (re-derived from first principles)

This plan does **not** assume `IAmbientTransaction` is the correct root. It re-derives the abstraction from the problem the framework is actually solving, then maps a migration off the current Messaging-coupled design and PR #424's extraction.

---

## 1. The problem, stated minimally

> Run work `W` exactly once, only after the datastore unit-of-work `T` that `W` logically belongs to has **durably committed**; discard `W` if `T` rolls back.

- `W` ∈ { enqueue outbox message, enqueue background job (#270), invalidate cache, publish domain event }.
- `T` ∈ { EF `SaveChanges` + commit, raw ADO transaction, SQL Server *out-of-band* commit, future Cosmos batch, future Mongo session }.

### What varies across providers and consumers
1. **Who drives `T`'s commit** — the application (EF `SaveChanges`, `using var tx`), or out-of-band machinery (SQL Server diagnostic).
2. **How "commit happened" becomes observable** — EF `IDbTransactionInterceptor.TransactionCommitted`, SqlClient `DiagnosticListener`, inline after our own commit call, a Cosmos batch response.
3. **What `W` needs from `T`** — nothing (cache invalidation), or the live `(DbConnection, DbTransaction)` to write durable rows *inside* `T` (Wolverine-style outbox), or a Cosmos batch handle.
4. **Durability of `W` between "registered" and "drained"** — in-memory (lost on crash after commit, before drain) vs durable rows (crash-safe).
5. **Nesting** — `W` registered in an inner scope must follow the **outer** scope's commit/rollback.

### What is invariant — i.e. the actual primitive
A **coordination scope** with a lifecycle: it begins, accumulates outcome-keyed callbacks from any number of participants, reaches **exactly one** terminal outcome (`Committed | RolledBack`), and invokes the matching callbacks **exactly once per coordinator instance**. The work is `Func<context, ct, ValueTask>` keyed by outcome.

**Guarantee scope (terminology, load-bearing).** The coordinator guarantees **exactly-once callback *invocation*** and an **exactly-once terminal *transition*** — within one process, for one coordinator instance. It does **not** guarantee exactly-once *business effect*: if a callback publishes to a broker and the process crashes before the broker acks, the coordinator cannot know whether the publish landed. That is an **at-least-once** delivery concern owned by the consumer (durable store + relay + idempotent consumers), not by the coordinator. Every "exactly once" in this plan means *callback invocation / terminal transition*, never *side effect*.

**Nothing in that invariant mentions a database, a transaction handle, or a provider.** The root abstraction is therefore a *commit coordinator*, not an *ambient transaction*. The transaction is a **participant detail** owned by a provider adapter, surfaced (only when needed) through a capability.

This is the load-bearing inversion of #265.

---

## 2. Architectural recommendation

> **Shipped deviations from this plan (kept for the design record; the code is the source of truth):**
> - `CommitCoordinatorState` shipped as `{ Active, Committed, RolledBack }` — the `Draining` value was collapsed into the terminal states. The terminal transition is a single `Interlocked.CompareExchange` on `Active → Committed/RolledBack` that snapshots the work lists; there is no observable intermediate `Draining` state.
> - The ambient stack is an `AsyncLocal` chain of linked-list frames (`CommitScopeFrame(Coordinator, Parent)`), not `AsyncLocal<ImmutableStack<…>>`. Same parent-restore semantics; lighter per-push allocation.
> - The `TryGetCapability` sketches below are illustrative pseudocode; the shipped `CommitContext.TryGetCapability` / `CommitCoordinator.TryGetCapability` do a real dictionary lookup keyed by capability contract.
>
> The agent-facing surface (`docs/llms/commit-coordination.md`) reflects the shipped shapes.

### 2.1 The core (datastore-agnostic, BCL-only)

```csharp
namespace Headless.CommitCoordination;

public enum CommitOutcome { Committed, RolledBack }

public enum CommitCoordinatorState { Active, Draining, Committed, RolledBack }

// CONSUMER-FACING. Register-only. No transaction, no lifecycle control.
[PublicAPI]
public interface ICommitCoordinator
{
    CommitCoordinatorState State { get; }

    // Deregisterable (AC-04): returns a handle so a participant can cancel its enlistment.
    IDisposable OnCommit(Func<CommitContext, CancellationToken, ValueTask> work);
    IDisposable OnRollback(Func<CommitContext, CancellationToken, ValueTask> work);

    // Typed participant buffer — no runtime cast, no provider coupling.
    // Constrained to ICommitWorkBuffer so this is a WORK-BUFFER registry, NOT a general object bag.
    TBuffer GetOrAdd<TBuffer>(Func<ICommitCoordinator, TBuffer> factory) where TBuffer : class, ICommitWorkBuffer;

    // Capability escape hatch — relational handle etc., for participants that need it.
    bool TryGetCapability<TCapability>([NotNullWhen(true)] out TCapability? capability)
        where TCapability : class, ICommitCapability;
}

// Ambient access. Read-only Current; the stack is managed by Begin/Dispose.
[PublicAPI]
public interface ICurrentCommitCoordinator
{
    ICommitCoordinator? Current { get; }
}

// Passed to every OnCommit/OnRollback callback. BCL-only.
[PublicAPI]
public sealed class CommitContext
{
    public required IServiceProvider Services { get; init; }
    public required CommitOutcome Outcome { get; init; }
    public bool TryGetCapability<TCapability>([NotNullWhen(true)] out TCapability? capability)
        where TCapability : class, ICommitCapability
    {
        capability = default;
        return capability is not null;
    }
}

public interface ICommitCapability;

// Marker for scope-local work buffers. Bounds GetOrAdd to deferred-work state, not arbitrary objects.
public interface ICommitWorkBuffer;

// The relational escape hatch — BCL-only, reuses the prior IAmbientDbTransactionResolver decision.
[PublicAPI]
public interface IRelationalCommitContext : ICommitCapability
{
    DbConnection? Connection { get; }
    DbTransaction? Transaction { get; }
}
```

**Concurrency and terminal-state semantics (required, not implied):**
- **Thread-safe registration.** Concurrent publishes can enlist on the same coordinator. `OnCommit`/`OnRollback` append under a lock (or lock-free list); `GetOrAdd<TBuffer>` is backed by a `ConcurrentDictionary<Type, object>` with single-factory-execution semantics. The terminal transition `Active → Draining` is a single `Interlocked.CompareExchange` that snapshots the work lists; the drain iterates the snapshot, so a late enlist cannot mutate the draining set.
- **Enlist-after-terminal throws.** Once `State != Active`, `OnCommit`/`OnRollback`/`GetOrAdd` throw `InvalidOperationException` ("commit scope already <state>"). This closes the TOCTOU window (R2 / old `RegisterCommitWork` after drain-started) — there is no silent stranding because there is no silent enlist.
- **`GetOrAdd` is scope-local state, not a service locator.** It is a `Dictionary<Type, ICommitWorkBuffer>` with a nicer API and a hard purpose: hold a participant's per-scope deferred-work buffer for the lifetime of one commit scope. The `ICommitWorkBuffer` constraint stops it becoming a general object bag (the ABP-`Items` failure mode the "stay narrow" constraint forbids). It does **not** resolve dependencies, span scopes, or outlive the coordinator. Buffers are created by their owning consumer's factory, owned by the coordinator, and disposed on terminal.
- **Capability registration.** Capabilities are **not** registered ad-hoc by consumers. The owner/signal source that constructs the coordinator populates an immutable capability set at `Begin` (the relational provider supplies `IRelationalCommitContext`; InMemory supplies none). `TryGetCapability<T>` is a read-only lookup over that fixed set — no mutation surface, no thread-safety concern. **Activation scope:** the capability seam is *designed* in v1 but only *exercised* at M5/β (durable jobs) and by the SqlServer detach path; v1 in-memory outbox does not read it. This is deliberate forward-design, called out so it is not mistaken for unused abstraction (see §9 Q on premature-abstraction).

### 2.2 The owner/signal side (privilege-split, internal-to-owner)

Consumers must not be able to commit or roll back the scope (least-privilege; today `OutboxMessageWriter` *could* call `Rollback()` on the shared contract). The terminal signal is a separate capability held only by whoever opened the scope.

```csharp
// Held by the SCOPE OWNER (Begin extension, EF interceptor adapter, middleware). Not consumer-facing.
// Both IAsyncDisposable AND IDisposable so synchronous `using` blocks compile (deferred-Q2 fix).
public interface ICommitScope : IAsyncDisposable, IDisposable
{
    ICommitCoordinator Coordinator { get; }
    ValueTask SignalAsync(CommitOutcome outcome, CancellationToken ct);
}

// Identity used to correlate a DETECTED native signal back to its coordinator.
public sealed class CommitCoordinatorBindings
{
    public required IServiceProvider Services { get; init; }   // captured at Begin; kept alive until drain
    public DbConnection? Connection { get; init; }             // correlation key for raw-ADO sources
    public object? ProviderTransactionKey { get; init; }       // e.g. SqlServer ClientConnectionId, EF transaction id
}

// Provider adapter that turns a provider's NATIVE commit/rollback edge into a SignalAsync call.
public interface ICommitSignalSource
{
    ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken ct);
}
```

A terminal signal arrives via one of two paths, both funnelling into `SignalAsync` → the coordinator's idempotent, exactly-once drain:

- **Driven** — the owner calls `SignalAsync(Committed)` (InMemory; explicit `using var tx` where the framework owns the commit).
- **Detected** — an `ICommitSignalSource` observes commit and raises it (EF interceptor `TransactionCommitted`; SqlClient diagnostic). **SQL Server's out-of-band path is now just one `ICommitSignalSource` implementation** — structurally identical to every other provider from the coordinator's point of view. This is Design Goal 2 satisfied by construction.

**Correlation registry (the generalized, principled `TransBuffer`).** A *detected* signal fires on an arbitrary thread carrying only provider identity (e.g. SqlClient's `ClientConnectionId`), not a coordinator reference. Each detecting `ICommitSignalSource` owns a private correlation registry keyed by `ProviderTransactionKey` → `ICommitScope`, populated at `Attach` and **removed on every exit path** (commit, rollback, dispose) to prevent leaks. The EF interceptor correlates by the intercepted `DbTransaction`; the SqlServer diagnostic source by `ClientConnectionId`. Inline sources (Npgsql, InMemory) need no registry — they hold the scope directly.

**Captured service scope (disposed-services fix).** `CommitContext.Services` must resolve during the drain, which for out-of-band runs **after** the request returns. The owner therefore hands the coordinator the service scope at `Begin` via `CommitCoordinatorBindings.Services`, and the **scope owner must not dispose that service scope until the terminal drain completes** — for driven flows that's lexical; for detected flows the signal source keeps the coordinator (and thus the captured scope) rooted in its correlation registry until drain, then disposes both. Conformance asserts that a scoped service resolved inside an `OnCommit` callback succeeds on the out-of-band path.

### 2.3 Ambient scope = a stack, not a slot

```csharp
internal sealed class CommitScopeStack
{
    private static readonly AsyncLocal<ImmutableStack<CommitCoordinator>?> _stack = new();
    public CommitCoordinator? Current => _stack.Value is { IsEmpty: false } s ? s.Peek() : null;
    public IDisposable Push(CommitCoordinator c) { _stack.Value = (_stack.Value ?? ImmutableStack<CommitCoordinator>.Empty).Push(c); return ...; }
    // Dispose pops and restores the parent (copy-on-write; immutable stack keeps child contexts isolated).
}
```

Nesting policy (default **join**, opt-in **new**):
- `Begin()` joins the ambient scope when one is active for the same physical unit-of-work: returns a **child** coordinator whose `OnCommit` work **promotes to the parent** and only fires on the **root** terminal signal.
- `BeginNew()` opens an **independent root** with its own signal source (its own physical transaction).
- **Child rollback dooms the root (corrected — was a correctness bug).** In a join there is **one** physical transaction; a child cannot selectively discard its post-commit work while its DB writes still commit with the root — that would run a committed write with no post-commit work, or worse, drop the work for data that *did* commit. So a child `SignalAsync(RolledBack)` sets the root **rollback-only** (TransactionScope "doomed transaction" / ABP failure-propagation semantics): the physical transaction rolls back and **all** work (parent + child) is discarded. Independent inner rollback (child rolls back, parent commits) is **only** available via savepoints — a future `IRelationalSavepointContext` capability and `BeginNew()` over a savepoint, never the default join.

**AsyncLocal lifetime footgun.** A detached background task (`Task.Run` without suppressing flow) captures the stack as-of-fork and can outlive the scope. Mitigation: the coordinator carries a `_disposed`/terminal flag, so a captured reference is **inert** (enlisting on a terminated coordinator throws — see §2.1), not a leak of live state; and consumers must not enlist from detached background tasks. Copy-on-write means the fork sees its own stack snapshot, so popping in the parent never corrupts the child and vice-versa.

**Join behavior matrix (the precise semantics — "same physical unit-of-work" pinned down).** A nested `Begin()` *joins* only when the ambient coordinator is bound to the **same physical connection/transaction**; otherwise it is a misuse (or requires `BeginNew()`). Behavior per concurrency shape:

| Shape | What happens | Why |
|---|---|---|
| **Same async flow** — `Begin()` inside an active scope, linear `await` | Returns a **child**; its `OnCommit` work promotes to the root; fires on the root's terminal signal. Disposal is LIFO. | Single execution context; the stack push/pop is well-ordered. |
| **Awaited child flow** — `await Child()` that calls `Begin()` | Same as above; child completes (and pops) before the parent continues. | Linearized by the `await`. |
| **Parallel tasks** — `Task.WhenAll(A(), B())`, both call `Begin()` | Each task forks the ambient stack copy-on-write, so each sees the **root** as parent and pushes onto its **own** branch (no cross-visibility). Both enlist on the **same root coordinator** — which is thread-safe (§2.1). **But** if A and B both touch the **same physical `DbConnection`/`DbTransaction`** concurrently, that is a **caller error**: ADO.NET connections are not concurrency-safe, independent of this framework. The coordinator makes enlistment safe; it does **not** make the underlying transaction concurrent. Recommendation: a parallel branch that needs its own transaction must `BeginNew()` (own connection). | AsyncLocal isolates the *ambient slot*; it cannot isolate a *shared mutable DB handle*. |
| **Disposal order** — child disposed after parent | The stack pop expects the disposing coordinator at the top; an out-of-order/double pop is detected and throws (`InvalidOperationException` "commit scope disposed out of order"). | Catches the classic ambient-scope misuse early instead of silently corrupting `Current`. |
| **Rollback propagation** — child `SignalAsync(RolledBack)` | Dooms the root (§2.3): the physical transaction rolls back, all work discarded. Root rollback likewise discards promoted child work. | One physical transaction has one fate. |

Conformance covers each row; the parallel-tasks row is asserted as "enlistment is thread-safe; sharing one physical transaction across parallel branches is rejected/documented as caller error."

### 2.4 Durability is a per-consumer policy, not a global choice

The coordinator core is durability-agnostic — it only runs `OnCommit` callbacks. **What** a callback does is the consumer's concern, behind `GetOrAdd<TBuffer>`:

- `InMemoryWorkBuffer` — accumulates work, drains on the commit signal (**Path α**). Loss window between commit and drain is covered by the consumer's own recovery (the existing background relay for messaging).
- `DurableWorkBuffer<TRow>` — writes rows **eagerly, inside `T`, at enlist time** via `IRelationalCommitContext` (the transaction is still open then; `OnCommit` fires *after* commit, which is too late to write into `T`). The post-commit `OnCommit` callback only **nudges the relay** ("rows N..M are now committed, dispatch them"); the relay also sweeps independently for crash recovery (`owner_id=0`). This is the Wolverine/Brighter model — the row's fate is tied to `T` by the database, not by a callback. Batched (single round-trip) durable writes are a future optimization via an optional pre-commit flush hook **only if measurement justifies it**; v1 β writes eagerly per enlist.

**The α/β distinction is really "does the consumer already own a durable store?"** Verified against current code: `OutboxMessageWriter._PublishInternalAsync` already calls `StoreMessageAsync(…, currentTransaction.DbTransaction, …)` — the message row is INSERTed into the published table **inside the ambient transaction** (committing atomically with business data) **before** `AddToSent` buffers it in memory. So messaging is *already* β-for-persistence (durable row in-tx) **plus** α-for-dispatch (in-memory post-commit trigger). The in-memory buffer is a **dispatch accelerator, not the source of truth**; a crash after commit is recovered by the relay re-scanning `StatusName.Scheduled` rows. **This makes α crash-safe for messaging** (Open Question #1 resolved).

**Recommendation: support both via policy; ship α-dispatch-on-durable-rows first.** Each consumer picks. Messaging keeps its current shape (durable row in-tx + in-memory dispatch trigger) — the coordinator just replaces the buffering/commit-detection plumbing. Jobs (#270) needs the same durable-row-in-tx guarantee but has **no** pre-existing durable store, so it uses `DurableWorkBuffer<TRow>` / fail-closed — a job that runs without its triggering data committed is a correctness violation, not recoverable lag. The capability model supplies the relational handle the durable buffer writes through, so β adds **no** core surface.

---

## 3. Decision log

D0. **Package family `Headless.CommitCoordination.*`, not `Headless.AmbientTransactions.*` or `Headless.Coordination.*`.** `Headless.Coordination.*` is already taken (node membership / liveness — `Headless.Coordination.{Abstractions,Core,PostgreSql,Redis,SqlServer}` exist). The name must say *commit* coordination to avoid a hard collision. Consumer ergonomics keep transaction words where honest (`BeginCoordinatedTransactionAsync`), but the **root contract is the coordinator**.

D1. **Root = `ICommitCoordinator` (register-only), not `IAmbientTransaction`.** The invariant (§1) contains no transaction; putting one on the root caused every R1 finding (handle leakage, public-setter corruption, nested orphaning). Removing it makes those states unrepresentable. Six independent ideation frames converged here.

D2. **Privilege split: `ICommitCoordinator` (consumers, register) vs `ICommitScope` (owner, signal).** Consumers cannot commit/rollback. Eliminates the "wrong actor decides outcome" class by construction.

D3. **`ICommitSignalSource` normalizes commit detection; SQL Server out-of-band becomes first-class.** The only thing that legitimately differs per provider is *how a commit is observed*. One async, fault-observing drain consumes every source identically. This deletes the `NoopTransaction` sentinel, `CompleteExternally()`, and the sync-over-async / TOCTOU root (R2). EF uses `IDbTransactionInterceptor.TransactionCommitted` (true post-commit; **not** `ISaveChangesInterceptor.SavedChanges`, which fires before an explicit `CommitAsync`).

D4. **Ambient = `AsyncLocal<ImmutableStack<…>>` with parent restore.** The BCL idiom (`Activity.Current`, `TransactionScope`). Fixes nested orphaning + public-setter corruption structurally. Default join, opt-in `BeginNew`.

D5. **Deferred work = typed registry `GetOrAdd<TBuffer>` on the coordinator.** Removes the runtime cast to `IOutboxMessageBuffer` and the singleton-writer `ConditionalWeakTable`. The buffer's lifetime *is* the scope, so no CWT global lock (PERF-02) and no GC-eviction message loss (REL-005). Jobs/cache/events each add a buffer type — zero provider or drain changes.

D6. **Capability model `TryGetCapability<T>` for provider-specific needs.** Keeps the root datastore-agnostic (Goal 5). `IRelationalCommitContext` returns BCL `(DbConnection?, DbTransaction?)` — reusing the established `IAmbientDbTransactionResolver` decision. InMemory returns `false`, proving agnosticism. Future `ICosmosBatchContext` / `IMongoSessionContext` grow as capabilities, never on the root.

D7. **Durability is per-consumer policy; α default, β-ready.** Don't force outbox to pay jobs' durability cost or jobs to accept outbox's loss window. The core stays durability-blind.

D8. **Un-signalled dispose = rollback (discard), never flush.** `TransactionScope.Complete` semantics: the dangerous path (flushing un-committed work) requires a positive signal; the safe path (discard) is automatic on any abnormal exit. Makes "job runs without its data" unreachable by default.

D9. **Terminal drain is exactly-once-per-instance, fault-aggregating, `CancellationToken.None`.** Interlocked terminal-state guard; run all participants even if one throws (fixes REL-002 stranding); aggregate faults; terminal writes use `None` (repo "terminal must-complete" idiom). The detected (out-of-band) path bridges sync→async via a TCS-gate with `RunContinuationsAsynchronously` — never sync-over-async. ("Exactly-once" = callback invocation, not business effect — §1 guarantee scope.)

D10. **Racing terminal signals: first wins, rest ignored + logged (first-class).** Multiple signals can race for one scope — e.g. an EF-on-SqlServer commit can fire **both** `IDbTransactionInterceptor.TransactionCommitted` **and** the SqlClient diagnostic; or a `Dispose` can race a late `Rollback`. The `Active → (Committed|RolledBack)` transition is a single `Interlocked.CompareExchange`: the **first** terminal signal wins and drives the drain; every subsequent signal is a **no-op that is logged at warning** (`"commit scope already <state>; ignoring <signal>"`). This is not an internal detail — it is a named acceptance criterion, because silent double-signal handling is exactly where provider bugs become undiagnosable.

D11. **SQL Server diagnostic self-probe is optional observability, warning-only by default.** The raw-ADO SqlServer source stays (real consumers commit through raw `SqlConnection`/Dapper with no EF interceptor), but its diagnostic is a **low-latency commit signal, not the durability mechanism** — the durable row committed in-tx + relay polling is the source of truth (D7, §2.4). An **opt-in** startup self-probe runs a throwaway commit and asserts the diagnostic fires; on failure it **logs a loud warning and degrades health by default**, and throws **only** when the app explicitly configures a strict failure mode. Rationale: making probe failure fatal by default is bad framework ergonomics — every consumer would have to supply probe connection material and accept startup coupling — for a signal that is recoverable by construction. Apps that need fast post-commit dispatch enable the probe; correctness never depends on it. (Resolves the #12 open question; supersedes the "log a loud warning" sketch in §6.)

D13. **SqlServer scope correlation by `ClientConnectionId` is sufficient; a per-attach generation token is deferred.** The out-of-band SqlServer source keys scopes by the connection's `ClientConnectionId`. `Attach` throws if a live scope already holds a key (no two live scopes per key); the dispose path is remove-if-equal (a scope only evicts its own entry); the signal path removes by key. The residual race is theoretical — a stale/duplicated commit diagnostic for an already-drained transaction arriving after a successor reused the same pooled `ClientConnectionId` could signal the successor early. Since the diagnostic is acceleration-only (D7, D11) and the successor's own durable rows govern correctness, the impact is bounded to early dispatch on the relay-recoverable path, never lost or duplicated durable work. Adding a per-attach generation token to the key would close the window but is deferred: it is not exploitable without a confirmed duplicate-diagnostic-event source, and the durable-row + relay invariant already protects correctness. Documented as a known limitation on `SqlServerCommitSignalSource`. Revisit if a real duplicate-event source is observed.

D12. **Roslyn analyzer for un-signalled inline-provider enlistment is deferred; helper-first ergonomics are the current guardrail.** The PostgreSQL provider is inline/caller-driven (Npgsql exposes no commit diagnostic), so a hand-rolled `EnlistCommitCoordination` that never calls `SignalAsync(Committed)` drains as rollback on dispose (D8) and silently discards accelerator-only work. The structural mitigation shipped instead of an analyzer: `ExecuteCoordinatedTransactionAsync` helpers weld open + enlist + commit + signal into one call (the signal cannot be forgotten) and are documented as **the recommended path** on every provider; raw enlistment is documented as the advanced seam with a loud PG-specific warning. A Roslyn analyzer flagging `EnlistCommitCoordination` calls without a reachable `SignalAsync` on the commit path remains future work — deferred because the flow analysis is non-trivial (the signal may live in a helper or extension method), the framework has no analyzer-package infrastructure yet, and the welded helpers shrink the exposed surface to deliberate advanced usage. Revisit if real consumers hand-roll PG enlistment despite the docs.

---

## 4. Comparison against the current #265 plan

### Concepts that REMAIN
- BCL-only `*.Abstractions`; relational impl behind a provider boundary.
- Per-provider packages + a `Tests.Harness` conformance suite (drain-exactly-once, detach→external-commit, rollback-discards, cancellation).
- `IOutboxBus` / `IOutboxQueue` consumer names unchanged.
- Greenfield, no back-compat shims.
- Provider-policy drain timing — now **formalized** as distinct `ICommitSignalSource` implementations rather than divergent base-class branches.
- EF ambient package depends on `EntityFrameworkCore.Relational` only (cycle-free); resolver impl stays out of the abstraction.

### Concepts that should be REMOVED
- `IAmbientTransaction` as the root abstraction → `ICommitCoordinator`.
- `object? DbTransaction { get; set; }` on the contract (untyped, side-effecting setter).
- `CompleteExternally()` on the universal contract.
- `NoopTransaction` sentinel pushed through `DbTransaction` to signal "drain now."
- The runtime cast `currentTransaction is not IOutboxMessageBuffer → throw`.
- `AutoCommit` on the universal contract (it conflated "is transactional?" with "commit now?").
- Flat `ICurrentAmbientTransaction.Current` slot.
- The singleton writer's `ConditionalWeakTable<IAmbientTransaction, MessageOutboxBuffer>`.

### Concepts that should be REDESIGNED
- Ambient accessor: flat slot → `AsyncLocal<ImmutableStack>` with parent restore.
- Deferred work: writer-owned CWT → coordinator-owned typed `GetOrAdd<TBuffer>`.
- Commit detection: per-provider divergence (EF inline vs SqlServer diagnostic+NoopTransaction) → uniform `ICommitSignalSource` feeding one async drain.
- `IAmbientDbTransactionResolver` (pull-side accessor) → `IRelationalCommitContext` capability via `TryGetCapability`. (Unifying with AuditLog's existing `IAmbientDbTransactionAccessor` stays a deferred follow-up, matching #265's U8.)
- `RegisterCommitWork(Func…)` returning `void` → `OnCommit/OnRollback` returning `IDisposable` (deregisterable).

---

## 5. Migration strategy (incremental, behind green tests at each step)

M1. **Land the core, no consumers touched.** `Headless.CommitCoordination.{Abstractions,Core,InMemory}` + `Tests.Harness`. Coordinator + ambient stack + capability model + `InMemoryWorkBuffer` + drain state machine. InMemory `ICommitSignalSource` (driven). Conformance: enroll→commit, drain-exactly-once, rollback-discards, nested join/promote, un-signalled-dispose-discards, cancellation.

M2. **Relational providers.** `.EntityFramework` (interceptor signal source + `IRelationalCommitContext` + `BeginCoordinatedTransaction[Async]` on `DatabaseFacade`), `.SqlServer` (raw ADO + SqlClient diagnostic signal source — the out-of-band path as a first-class source), `.PostgreSql` (Npgsql inline source). Run the harness across all four providers; add the **detach→external-commit** conformance scenario specifically against the SqlServer source.

M3. **Re-point Messaging.** `OutboxMessageWriter` becomes stateless: resolves `ICurrentCommitCoordinator.Current`, `GetOrAdd<MessageOutboxBuffer>(…)`, registers its drain via `OnCommit`. Delete `IOutboxTransaction`, `CompleteExternally`, `NoopTransaction`, the cast. Keep `IOutboxBus/IOutboxQueue`. **Rewrite** (not rename) the messaging test fakes — they encode the dead `AddToSent`-on-transaction contract.

M4. **Capstone parity.** One message + one EF domain write commit atomically through a single coordinated transaction and roll back together; SqlServer out-of-band commit drains the buffer exactly once. Existing messaging + audit-log suites green.

M5. **(Separate, #270) Jobs.** Jobs references `.Abstractions` + `.DurableWork`; defines `JobWorkBuffer : DurableWorkBuffer<TimeJobRow>` on the same coordinator, fail-closed (`OnProviderMismatch = Throw`). No core change; `.DurableWork` may land earlier if messaging opts into durable buffering, but messaging's existing durable-row path means it is not required for M3.

---

## 6. Risk analysis

- **SQL Server commit semantics.** Out-of-band commit fires a **synchronous void** SqlClient diagnostic; the DB is *already committed* when it fires, so the committer cannot await the drain. Mitigation: the `SqlServerCommitSignalSource` schedules the async drain onto a bounded channel (TCS-gate, `RunContinuationsAsynchronously`) and the relay is the recovery path — never sync-over-async, never block the diagnostic thread. The detach-without-dispose lifecycle (`Transaction == null` at signal time) is modeled as the source raising `SignalAsync(Committed)` on a coordinator whose relational capability is already detached; the capability returns `null` handles, which is correct (work that needed the handle ran during enlistment, not at drain).
- **EF Core integration.** Use `IDbTransactionInterceptor.TransactionCommitted/RolledBack` (true post-commit on explicit transactions). Do **not** key off `ISaveChangesInterceptor.SavedChanges` (fires before an explicit `CommitAsync`). Note efcore#32750: a `DbUpdateConcurrencyException` may not raise `SaveChangesFailed` — so discard is driven by the **rollback signal**, never by inferring failure from SaveChanges.
- **Nested ambient scopes.** Immutable-stack copy-on-write isolates child execution contexts. Join-by-default; child commit promotes to parent; **child rollback dooms the root physical transaction** (corrected — selective child discard while the physical tx commits is a silent-inconsistency bug, §2.3). Savepoint-isolated independent inner rollback is a future `IRelationalSavepointContext` capability, not core. Detached background tasks must not enlist (AsyncLocal capture); the coordinator's terminal flag makes a captured reference inert (throws), not a live-state leak.
- **Memory ownership.** Buffer lifetime = coordinator lifetime = scope lifetime (no CWT). The coordinator owns its buffers and disposes any `IDisposable`/`IAsyncDisposable` buffer on terminal. Terminal drain nulls references under the terminal lock; double-dispose idempotent; generation/Interlocked guard on the terminal transition. Un-signalled dispose discards. **Captured service scope:** the owner hands the coordinator the DI scope at `Begin` and must not dispose it until the terminal drain completes — for out-of-band the signal source roots the coordinator (and scope) in its correlation registry until drain, so an `OnCommit` callback never resolves a disposed scoped service.
- **Fault observation.** Drain runs **all** participants, aggregates faults, surfaces `AggregateException` after all ran (no stranding). Out-of-band drains observe faults explicitly (awaited inside the scheduled task; failures logged + left for relay recovery).
- **Crash recovery.** Path α: consumer's own durable store + relay (messaging already has this). Path β: rows committed inside `T`, relay sweeps with `owner_id=0`-style recovery. The core makes **no** crash-safety claim — it is explicit per consumer policy (D7), documented, not implied.
- **Future Jobs (#270).** Jobs is β/fail-closed on the same coordinator. The one cross-cutting decision Jobs forces *now*: `OnProviderMismatch` must default to `Throw`, because silent fallback (correct for at-least-once outbox) means a job fires against uncommitted data. Surface this as an explicit policy on the durable buffer, not a hidden default.
- **Detected-signal fragility.** The SqlServer source depends on `Microsoft.Data.SqlClient` diagnostic event names / property shapes (internal-ish) and can drift across driver upgrades. Mitigation: pin the contract in a single source, and add an **opt-in** startup self-probe that asserts the diagnostic fires once on a throwaway commit — **warning/health-degraded by default, strict-throw only when the app opts in (D11)** — so detection failure is observable, not silent. Because the diagnostic is an accelerator and not the durability mechanism (D7, §2.4), a missed signal degrades dispatch latency, never durability. The EF interceptor path (preferred where EF is in play) has no such fragility.
- **PostgreSQL has no out-of-band detection.** Npgsql commit detection is **inline** (the source fires when our own `Commit`/`SaveChanges` runs); bypassing the framework's transaction API (raw `NpgsqlTransaction.Commit`) means the in-memory accelerator never fires and messages wait for the background sweep. This is acceptable for α (relay recovers) and documented as a "use the coordinated `Begin` extension" requirement; it is **not** acceptable for β jobs, which write rows eagerly in-tx and therefore do not depend on detection at all.
- **Rejected alternative — DB-native coordination (PG `LISTEN/NOTIFY`, table polling only).** Considered and rejected as the primitive: it is per-provider (no SqlServer `AFTER COMMIT` trigger), cannot drive in-process broker dispatch latency, and does not cover non-relational stores (Cosmos/Mongo) the capability model targets. DB-native remains available *inside* a durable buffer's relay as an implementation detail.

---

## 7. Package layout

```
Headless.CommitCoordination.Abstractions    BCL-only, PURE COORDINATION. ICommitCoordinator, ICurrentCommitCoordinator,
                                            CommitContext, CommitOutcome, ICommitCapability, ICommitWorkBuffer,
                                            IRelationalCommitContext, ICommitScope, ICommitSignalSource,
                                            CommitCoordinatorBindings, AND InMemoryWorkBuffer (BCL-only: a
                                            thread-safe work list — pure coordination, no persistence).
Headless.CommitCoordination.Core            CommitCoordinator, CommitScopeStack (AsyncLocal),
                                            drain state machine, correlation-registry helpers.
Headless.CommitCoordination.DurableWork     DurableWorkBuffer<TRow> base + IWorkRelay relay/recovery contract.
                                            SEPARATE from Core/Abstractions: persistence + rows + recovery are
                                            NOT pure coordination (keeps Core from drifting into workflow infra).
                                            Depends on .Abstractions (uses IRelationalCommitContext). Consumer-supplied
                                            concrete row + sweep query live in the consumer/provider package.
Headless.CommitCoordination.EntityFramework deps: Microsoft.EntityFrameworkCore.Relational ONLY (no Headless.Orm.EntityFramework → cycle-free).
                                            EF interceptor signal source, EF IRelationalCommitContext, Begin extensions.
Headless.CommitCoordination.SqlServer       Raw ADO + SqlClient DiagnosticListener signal source (out-of-band, first-class).
Headless.CommitCoordination.PostgreSql      Raw ADO / Npgsql inline signal source.
Headless.CommitCoordination.InMemory        Driven signal source; no relational capability (TryGetCapability → false).
Headless.CommitCoordination.Tests.Harness   Cross-provider conformance (interface+extensions shape per CLAUDE.md).
```

Dependency rule: **consumers depend on `.Abstractions`** (always) and **`.DurableWork`** (only if they need durable buffering). Messaging references `.Abstractions`, defines `MessageOutboxBuffer : InMemoryWorkBuffer`. Jobs references `.Abstractions` + `.DurableWork`, defines `JobWorkBuffer : DurableWorkBuffer<…>`. Providers reference `.Core`. No provider type is ever visible on the root contract.

**Boundary note (two corrections from review).** (1) The buffer base a consumer subclasses is part of the **consumer contract**, so `InMemoryWorkBuffer` lives in `.Abstractions`, not `.Core` — otherwise `MessageOutboxBuffer : InMemoryWorkBuffer` would force Messaging to reference `.Core`. (2) `DurableWorkBuffer<TRow>` drags **persistence, rows, recovery, and a relay** — those are *not* pure coordination, so it lives in a **separate `.DurableWork` package**, keeping `.Abstractions`/`.Core` from drifting toward workflow infrastructure. A consumer that only buffers in memory never references `.DurableWork`.

---

## 8. Acceptance criteria

- [ ] `Headless.CommitCoordination.{Abstractions,Core,InMemory,EntityFramework,SqlServer,PostgreSql}` + `Tests.Harness`, each with a README and a `docs/llms/commit-coordination.md` entry; a CONCEPTS.md entry defines *commit coordinator*, *commit signal source*, *work buffer*, *capability*.
- [ ] Root contract `ICommitCoordinator` exposes **no** `DbTransaction`, `Commit`, `Rollback`, `AutoCommit`, or `CompleteExternally`. No `NoopTransaction` type exists anywhere.
- [ ] Consumers register via `OnCommit/OnRollback` (returning `IDisposable`) and `GetOrAdd<TBuffer>`; **no runtime cast** of a coordinator/transaction to a buffer interface remains.
- [ ] Commit detection is a single `ICommitSignalSource` seam; SQL Server out-of-band, EF interceptor, Npgsql inline, and InMemory driven sources all feed **one** async fault-observing drain. No sync-over-async on any drain path (proven by a deadlock test under a `SynchronizationContext`).
- [ ] Ambient is `AsyncLocal<ImmutableStack>`; conformance proves: nested join promotes child work to the root; **child rollback dooms the root** (asserts the physical transaction rolled back and ALL work was discarded — parent and child); outer rollback discards all; parent slot is intact after a concurrent child async flow (isolation test).
- [ ] Enlisting (`OnCommit`/`OnRollback`/`GetOrAdd`) after the coordinator left `Active` throws `InvalidOperationException` (no silent stranding); concurrent enlist during the `Active → Draining` transition is covered by a stress test.
- [ ] An `OnCommit` callback can resolve a **scoped** service via `CommitContext.Services` on the SqlServer out-of-band path (asserts the captured DI scope outlives the request and is disposed only after drain).
- [ ] `DurableWorkBuffer<TRow>.OnProviderMismatch` defaults to `Throw`; a test enlisting durable work against a non-matching/absent `IRelationalCommitContext` throws at enlist time (fail-closed), and an opt-in `Warn` mode is covered for the at-least-once outbox case.
- [ ] `TryGetCapability<IRelationalCommitContext>` returns BCL `(DbConnection?, DbTransaction?)` on relational providers and `false` on InMemory.
- [ ] Drain is exactly-once **per coordinator instance** (callback invocation — not a business-effect guarantee, §1) and fault-aggregating: a test with two participants where the first throws asserts the second still ran and an `AggregateException` surfaced; terminal writes use `CancellationToken.None`.
- [ ] Racing terminal signals: **first wins, rest are logged no-ops.** A test that fires both `TransactionCommitted` and the SqlClient diagnostic for one EF-on-SqlServer commit (and a `Dispose` racing a `Rollback`) asserts a single drain and a warning log for each ignored signal.
- [ ] SqlServer diagnostic self-probe is **opt-in** and **warning/health-degraded by default** (D11): a probe failure logs a warning and reports degraded health without throwing; strict-throw is available only when the app explicitly configures it. A test asserts that suppressing/disabling the diagnostic does **not** lose work — the buffer still drains via the consumer's recovery sweep — proving correctness does not depend on the signal.
- [ ] Un-signalled dispose discards all work (no flush): test registers work, disposes the scope without a commit signal, asserts zero drains.
- [ ] Durability policy is per-consumer: `InMemoryWorkBuffer` and `DurableWorkBuffer<TRow>` both run on the same coordinator; the durable buffer writes rows through `IRelationalCommitContext` inside the transaction and is recovered post-commit by a relay.
- [ ] Capstone: a message + an EF Core domain write commit atomically through one coordinated transaction (and roll back together); the SqlServer out-of-band path drains the message buffer exactly once.
- [ ] All existing Messaging + AuditLog tests pass; messaging fakes rewritten to exercise the new enlistment path (not renamed).
- [ ] `Headless.CommitCoordination.EntityFramework` compiles with **no** reference to `Headless.Orm.EntityFramework`.

---

## 9. Open questions (decide before M3)

1. ~~**Durability default for Messaging.**~~ **RESOLVED (verified against code).** `OutboxMessageWriter._PublishInternalAsync` persists the message row inside the ambient transaction via `StoreMessageAsync(…, DbTransaction, …)` before the in-memory `AddToSent`; the relay recovers from `StatusName.Scheduled` rows. The in-memory buffer is a dispatch accelerator, not the source of truth → α is crash-safe for messaging. No change needed; jobs still require `DurableWorkBuffer` because they lack an equivalent store.
2. **`ICommitScope` exposure.** Should the owner-side scope be a separate package-internal type, or an internal interface in `.Core` that provider sources implement? Leaning internal-to-`.Core` with `InternalsVisibleTo` per provider package.
3. **AuditLog resolver unification (U8).** Fold the existing read-only `IAmbientDbTransactionAccessor` into `IRelationalCommitContext`, or leave it as a sibling pull-side accessor? Defer to a follow-up issue (matches #265).
4. **Nesting beyond join.** Is `BeginNew()` (independent physical transaction) needed for v1, or YAGNI until a consumer asks? Default: ship join-only, add `BeginNew` when #270 or a real caller needs it.
