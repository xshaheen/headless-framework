---
date: 2026-06-15
topic: jobs-commit-coordination-enqueue
---

# Atomic Job Enqueue via Commit Coordination

## Summary

Let `ITimeJobManager.AddAsync` enqueue a job inside the caller's active commit-coordination scope, so a domain write, a message publish, and a job enqueue commit atomically. When no coordinator is active, today's direct-insert behavior is unchanged.

## Problem Frame

Issue #270 was written against the `IAmbientTransaction` + `IAmbientWorkBuffer<TWork>` substrate from #265. That substrate was replaced by the `Headless.CommitCoordination.*` family (#428), so every interface name in the issue's "Proposed shape" is dead — but the intent survives.

Today `JobsManager._AddTimeJobAsync` calls `IJobPersistenceProvider.AddTimeJobs`, and the EF provider creates its **own** `DbContext` and `SaveChangesAsync` — independent of any caller transaction. A user who writes `dbContext.SaveChangesAsync()` then `timeJobManager.AddAsync(...)` has two uncoordinated writes: a crash or failure between them diverges the job row from domain state. Messaging already closed this gap through commit coordination; jobs is the remaining unintegrated write.

## Key Decisions

- **Follow the `OutboxMessageWriter` shape (Approach A), not `DurableWorkBuffer<TRow>` (Approach B), as the lean.** Messaging — the only existing commit-coordination consumer and the reference the issue cites — hand-rolls the integration: capture the coordinated context once before any await, write the row inside the ambient transaction, buffer the post-commit side effect. `DurableWorkBuffer<TRow>` covers only the row write and writes it immediately anyway, so it would split one logical operation across two mechanisms. The firm A-vs-B pick is provider-internal with identical observable behavior — deferred to planning.

- **Side effects move to post-commit.** `_AddTimeJobAsync` does more than insert a row: it immediately acquires-and-dispatches the job when `ExecutionTime <= now+1s`, restarts the host scheduler, and sends a notification-hub message. In a coordinated scope these must defer to commit — firing them before commit would dispatch a job whose row can still roll back, recreating the divergence bug inverted.

- **Coordinated path is EF-only.** A relational transaction is the prerequisite; the in-memory persistence provider cannot participate and stays direct-insert.

- **Tenancy capture is out of scope here.** Job entities have no `TenantId` column today (`BaseJobEntity` carries only `Function`, `Description`, `InitIdentifier`, timestamps; `LockHolder` is node ownership, not tenant). The issue's "tenant captured at buffer time" criterion presupposes #278 landing a tenant column first.

---

## Requirements

**Coordinated enqueue**

- R1. When a relational commit coordinator is active, `AddAsync` writes the job row inside that coordinator's transaction; the row commits atomically with the caller's domain writes and is not externally visible until commit.
- R2. On rollback, the buffered job is discarded — no row persists and no side effects fire.
- R3. Buffered jobs preserve insertion ordering on flush.

**Side-effect sequencing**

- R4. Immediate dispatch (`ExecutionTime <= now+1s`), host-scheduler restart, and the notification-hub send are deferred to post-commit and fire only after the transaction commits.

**Batch and cron**

- R5. `AddBatchAsync` follows the same coordinated routing as `AddAsync`.
- R6. `ICronJobManager.AddAsync` participates in the coordinated path when cron creation occurs inside a domain-write boundary; startup seeding (`MigrateDefinedCronJobs`) does not participate (handled by #267's distributed lock). Whether the cron-creation path is ever a real domain-write boundary is deferred to planning.

**Failure mode**

- R7. When a coordinator is active and exposes a relational capability whose transaction has completed/is unusable, `AddAsync` throws rather than silently doing a direct (non-atomic) insert. When the active coordinator exposes no relational capability at all (e.g. a messaging-only scope), `AddAsync` falls back to the direct path — commit coordination is an ambient scope any subsystem may open, so jobs must not make it infectious.

**Non-coordinated parity**

- R8. With no commit coordinator active, `AddAsync` / `AddBatchAsync` behave exactly as today — direct insert via `IJobPersistenceProvider`, with dispatch / scheduler-restart / notify in-band.
- R9. The in-memory persistence provider is not coordinator-aware and always uses the direct-insert path.

**Documentation**

- R10. `src/Headless.Jobs.Abstractions/README.md` (and its lockstep `docs/llms/jobs.md`) gain a commit-coordination usage example: domain write + message publish + job enqueue committing atomically in one scope.

---

## Acceptance Examples

- AE1. Atomic commit. **Covers R1, R2, R4.**
  - **Given:** A caller opens an EF transaction and enlists commit coordination, then issues a domain write, a `PublishAsync`, and an `AddAsync`.
  - **When:** the transaction commits.
  - **Then:** all three persist atomically; the job's dispatch/scheduler/notify side effects fire only after commit.
  - **And on rollback:** none of the three persist and no job side effect fires.

- AE2. Insertion ordering. **Covers R3.**
  - **Given:** two `AddAsync` calls inside one coordinated scope.
  - **Then:** the rows flush in call order.

- AE3. Coordinator failure modes. **Covers R7.**
  - **Given:** a coordinator is active with a relational capability whose transaction has completed.
  - **When:** `AddAsync` is called.
  - **Then:** it throws, surfacing the mis-wire rather than enqueuing non-atomically.
  - **And given:** a coordinator is active with no relational capability at all — `AddAsync` falls back to a direct insert without throwing.

- AE4. No coordinator. **Covers R8.**
  - **Given:** no commit coordinator is active.
  - **Then:** `AddAsync` direct-inserts and dispatches in-band, identical to current behavior.

---

## Scope Boundaries

- Update / Delete inside a coordinated scope — only enqueue (`AddAsync` / `AddBatchAsync`).
- Cron seeding (`MigrateDefinedCronJobs`) via commit coordination — startup, handled by #267.
- Commit coordination on job execution paths — jobs run in their own workers.
- Tenancy stamping inside the coordinated write — blocked on #278 adding a tenant column; no `TenantId` exists today.

## Dependencies / Assumptions

- Depends on `Headless.CommitCoordination.*` (#428, shipped). The #265 `IAmbientTransaction` substrate the issue references is obsolete.
- The EF jobs provider currently creates its own `DbContext`; the coordinated path requires writing into the caller's ambient `DbConnection` + `DbTransaction` instead — the EF-entity-into-foreign-connection mechanism is a planning-time question.
- Routing splits across layers: the provider-agnostic `JobsManager` must detect the coordinator and defer side effects, while the EF provider performs the in-transaction row write.

## Outstanding Questions

**Resolved during planning**

- Failure mode (R7): resolved to a **split** — throw only when a relational capability is present but its transaction is unusable; fall back to direct insert when no relational capability exists at all. This keeps the atomicity guarantee where the caller clearly expected it (a live/dead DB transaction) without making `AddAsync` throw inside an unrelated coordinated scope (e.g. messaging-only). Mirrors `OutboxMessageWriter`'s null-capture fallback, with `OutboxIntegrationEventDispatcher`'s hard error reserved for the dead-transaction case.

**Deferred to planning**

- A vs B mechanism: hand-roll like `OutboxMessageWriter` (lean) vs `DurableWorkBuffer<TimeJobEntity>`. Decide via a spike on writing an EF entity into a foreign `DbConnection` + `DbTransaction`.
- Cron parity (R6): is cron creation ever inside a domain-write boundary in practice? If not, drop cron from scope.
- Layer ownership of the coordinated routing (manager in `Headless.Jobs.Core` vs EF provider).

## Sources / Research

- Issue #270 (intent; obsolete substrate names).
- `src/Headless.Messaging.Core/Internal/OutboxMessageWriter.cs` — routing reference (capture-once, store-in-tx, buffer side effect); the Approach A pattern.
- `src/Headless.Messaging.Core/Transactions/MessageOutboxBuffer.cs` — `InMemoryWorkBuffer` + `OnCommit` flush.
- `src/Headless.Orm.EntityFramework.Messaging/OutboxIntegrationEventDispatcher.cs` — EF coordinated save + fail-loud guard.
- `src/Headless.CommitCoordination.DurableWork/DurableWorkBuffer.cs` — Approach B candidate.
- `src/Headless.CommitCoordination.Abstractions/` — `ICurrentCommitCoordinator`, `ICommitCoordinator` (`OnCommit`), `IRelationalCommitContext`.
- `src/Headless.Jobs.Abstractions/Managers/JobsManager.cs` — `_AddTimeJobAsync` (the side effects to defer).
- `src/Headless.Jobs.EntityFramework/Infrastructure/JobsEFCorePersistenceProvider.cs` — own-DbContext write to extend.
