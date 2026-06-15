---
title: "AsyncLocal ambient scope stranded across await: coordinator path silently dead"
date: 2026-06-09
category: logic-errors
module: Headless.CommitCoordination
problem_type: logic_error
component: service_class
symptoms:
  - "ICurrentCommitCoordinator.Current is null in the caller right after an async enlist method returned a live scope"
  - "Outbox rows are dispatched to the broker before the database transaction commits (non-atomic publish)"
  - "Happy-path integration test stays green even though the coordinator branch never executes (false green)"
  - "The coordinated code path looks wired but is dead code — no callback ever drains on commit"
  - "Rolled-back transactions still leave their outbox effect visible (no discard on rollback)"
root_cause: async_timing
resolution_type: code_fix
severity: critical
related_components:
  - service_class
  - database
tags: [asynclocal, execution-context, async-await, ambient-scope, transactional-outbox, commit-coordination, false-green]
---

# AsyncLocal ambient scope stranded across await: coordinator path silently dead

## Problem

An `async` enlist method set the ambient commit-coordinator scope (`AsyncLocal<T>.Value`) and returned the live scope to its caller, but the caller's `ICurrentCommitCoordinator.Current` read back `null`. The coordinator branch of the outbox writer became dead code, so integration events were dispatched to the broker **before** the database transaction committed — the exact non-atomicity the transactional outbox exists to prevent. A happy-path test stayed green because it only asserted the outbox row was eventually present, which holds whether the write was transactional or autonomous.

## Symptoms

- `ICurrentCommitCoordinator.Current` is `null` in the caller immediately after an `async BeginCoordinatedTransactionAsync(...)` returned a non-null scope.
- The outbox writer's `_IsCoordinatedTransactional()` check returned `false`, silently routing to the immediate-dispatch fallback — messages hit the broker before commit.
- The happy-path integration test (`enlisted_publish_committed_should_persist_the_outbox_row`) passed regardless, because row-presence after commit is satisfied by both the atomic and the autonomous-write paths.
- A rolled-back transaction left no coordinator to discard the buffered dispatch, so the outbox effect could still escape.

## What Didn't Work

- **Trusting the green suite.** A subagent reported the cutover "fully green." It was — but the only test exercising the path asserted a post-condition (row present) that could not distinguish the coordinated path from the broken fallback. Green meant "nothing threw," not "the transaction and the dispatch are atomic."
- **Setting `AsyncLocal.Value` inside the `async` enlist method.** This is the root error. The value was set correctly *inside* the method's execution context, but that mutation never propagated back to the caller (see Why This Works). Making the method `async` was the trap — the signature looked harmless.

## Solution

Set the ambient scope **synchronously in the caller's own stack frame**, not inside an `async` callee. The enlist seam was rewritten from an `async Task<ICommitScope>` to a synchronous `ICommitScope`-returning extension, called directly in the save pipeline's frame:

```csharp
// BEFORE — broken: async method strands the AsyncLocal set across the await boundary.
public static async Task<ICommitScope> BeginCoordinatedTransactionAsync(
    this DatabaseFacade database, IServiceProvider services, CancellationToken ct)
{
    var tx = await database.BeginTransactionAsync(ct);
    var scope = factory.Create(...);   // sets AsyncLocal<CommitCoordinator>.Value
    return scope;                       // caller's ExecutionContext is restored on return → Value reverts to null
}

// AFTER — correct: synchronous enlist runs in the caller's frame, so the set persists and flows down.
public static ICommitScope EnlistCommitCoordination(
    this DatabaseFacade database, IDbContextTransaction transaction, IServiceProvider services)
{
    var bindings = new CommitCoordinatorBindings { Connection = ..., Transaction = transaction.GetDbTransaction() };
    return factory.Create(bindings);    // AsyncLocal set in caller's frame → persists, flows to awaited callees
}
```

Caller (the EF save pipeline) opens the transaction and enlists synchronously before awaiting the save:

```csharp
await using var tx = await Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
await using var coordination = Database.EnlistCommitCoordination(tx, serviceProvider); // sync set in THIS frame
return await _SaveWithinTransactionAsync(...);  // Current now flows into the awaited callee
```

The decisive new test asserts the **rollback-discard** invariant, which the happy-path test could not:

```csharp
// enlisted_publish_rolled_back_should_discard_the_outbox_row
// Publishes through the coordinator, then rolls the transaction back.
// Asserts the outbox row is ABSENT — proving the dispatch was buffered on the
// coordinator and tied to the commit edge, not written autonomously.
```

## Why This Works

`AsyncLocal<T>` is backed by `ExecutionContext`, which has **copy-on-write, caller-restored** semantics:

- When you `await` (or otherwise enter an `async` state machine), the runtime captures the current `ExecutionContext`. Mutations to `AsyncLocal.Value` *inside* the awaited method occur on a **copy**. When the method returns, the caller's original `ExecutionContext` is **restored** — so any `AsyncLocal.Value` the callee set is discarded from the caller's view. The callee's set flows *downward* (into anything it awaits), never *upward* back to the caller.
- A **synchronous** set executes in the caller's own `ExecutionContext` frame. There is no capture/restore boundary, so the set persists in the caller and flows *down* into everything the caller subsequently awaits — which is exactly where the coordinator must be visible (the save, the EF transaction interceptor's post-commit callback).

This is the same reason `TransactionScope` must be created with a `using` in the caller's frame rather than handed back from an `async` factory: ambient context that needs to flow to *later* awaited work has to be established *before* the await, in the frame that does the awaiting.

The fix also moved the true post-commit signal onto EF's `IDbTransactionInterceptor.TransactionCommitted`/`TransactionRolledBack` (genuine post-commit edges), so the buffered dispatch drains exactly-once on durable commit and is discarded on rollback.

## Prevention

- **Never set ambient `AsyncLocal` state inside an `async` method and expect the caller to observe it.** If a value must be visible to the caller and to the caller's *later* awaited work, set it synchronously in the caller's frame. Prefer a synchronous `IDisposable`/`IAsyncDisposable` scope object over an `async` factory for anything that establishes ambient context.
- **Distrust happy-path-only green.** A test that asserts a post-condition reachable by *both* the correct and the broken path proves nothing about the path. For atomicity/coordination invariants, add a **negative** test (rollback ⇒ effect absent) — it is the only assertion that distinguishes "buffered and tied to commit" from "written autonomously."
- **Investigate flagged unexpected results before declaring done.** The cutover was reported green; the buffered-dispatch design implied the coordinator *must* be observed on commit. "Green but the branch can't have run" is a contradiction worth chasing, not waving through.
- A focused guard test asserting `ICurrentCommitCoordinator.Current is not null` immediately after enlist, in the caller frame, would have caught the stranding directly.

## Related Issues

- [Commit coordination architecture (design context)](https://github.com/xshaheen/headless-framework/issues/265) — the design this fix completes.
- See also: `docs/solutions/architecture-patterns/caching-fail-safe-coordinator-design.md` — sibling coordinator design with related ambient/fail-safe trade-offs.
- Sibling logic-error in the same outbox path: `docs/solutions/logic-errors/terminal-state-overwrite-on-redelivery-2026-05-16.md`.
