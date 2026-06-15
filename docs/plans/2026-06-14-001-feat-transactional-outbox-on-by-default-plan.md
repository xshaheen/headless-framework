---
title: "feat: Transactional outbox on-by-default for the EF-context messaging path"
type: feat
date: 2026-06-14
branch: xshaheen/ideate-ambient-transaction-design
pr: 428
origin: docs/plans/2026-06-08-002-feat-commit-coordination-architecture-plan.md
status: ready
---

# feat: Transactional outbox on-by-default for the EF-context messaging path

## Summary

Today, choosing the EF storage path for messaging (`setup.UseEntityFramework<TContext>()` inside `AddHeadlessMessaging`) does **not** enable the transactional outbox. The outbox writer reads `ICurrentCommitCoordinator.Current`, which resolves to `MessagingNullCommitCoordinator` (always `null`) unless the consumer *separately* registers a commit-coordination provider **and** wires the EF interceptor into their `DbContext`. Miss either step and publishes silently take the non-transactional immediate-dispatch path — the outbox row is written on its own connection and dispatched immediately, surviving a rollback. The API reads identically whether coordination is wired or not, so the missing atomicity is invisible.

This plan makes the atomic outbox **on by default** for the EF-context path with **zero consumer wiring**, and makes any mis-wire **fail loud at startup** instead of degrading silently. The mechanism is a DI-registered `IDbContextOptionsConfiguration<TContext>` (EF Core 9+, verified on EF Core 10.0.8) that auto-attaches the commit interceptor to a plain `AddDbContext<TContext>`. A startup validation gate (modeled on the existing `*EntityValidationStartupGate<TContext>` + the `SqlServerCommitDiagnosticProbe` mode enum) proves the interceptor actually fires. The same `IDbContextOptionsConfiguration` mechanism then replaces the `AddDbContext`-lambda interceptor seam in `AddHeadlessDbContext`/`AddHeadlessIdentityDbContext`, so DI-registered interceptors reach consumers' own plain `AddDbContext<TContext>` too.

This is the messaging-facing payoff of the commit-coordination substrate built in the origin plan (`docs/plans/2026-06-08-002-...`, decisions D1-D13).

---

## Problem Frame

**Who is affected:** any consumer using `AddHeadlessMessaging(setup => setup.UseEntityFramework<TContext>())` who publishes inside a database transaction expecting atomicity.

**The footgun (today):**
- `AddHeadlessMessaging` registers only `services.TryAddSingleton<ICurrentCommitCoordinator, MessagingNullCommitCoordinator>()` (`src/Headless.Messaging.Core/Setup.cs:284`). It never calls `AddCommitCoordination()`.
- The transactional outbox therefore requires three independent, invisible steps from the consumer: (1) register `AddCommitCoordination()` + the EF signal source; (2) attach the commit interceptor to their `DbContext`; (3) actually use the coordinated path. Omitting any of (1)/(2) silently yields non-atomic immediate dispatch.
- Prior-art research confirms this exact footgun profile across CAP and Brighter, and confirms the field's best answers are **single-call/auto registration + startup validation** (NServiceBus, Wolverine), not per-call ceremony (Headless already auto-enlists, so no per-call change is needed).

**The fix (this plan):** on the EF-context path, registering the storage *is* what turns the outbox on. The interceptor attaches automatically via `IDbContextOptionsConfiguration<TContext>`. A startup gate fails loud if the interceptor isn't firing. An explicit opt-out exists for the deliberate non-transactional case.

---

## Scope Boundaries

**In scope:**
- On-by-default transactional outbox for the **EF-context messaging path** (`UseEntityFramework<TContext>` in `Headless.Messaging.Storage.PostgreSql` / `.SqlServer`).
- Automatic EF commit-interceptor attachment via DI-registered `IDbContextOptionsConfiguration<TContext>`.
- An opt-out builder method.
- A startup validation gate (Warn default, Strict opt-in) that proves the interceptor fires.
- A framework-wide refactor of `AddHeadlessDbContext`/`AddHeadlessIdentityDbContext` interceptor wiring onto the same mechanism (same PR — see KTD-5).
- Docs + demo simplification reflecting the new default.

**Out of scope / unchanged:**
- **Raw-ADO storage paths** (`UsePostgreSql(connString)` / `UseSqlServer(connString)` without a `DbContext`) — no `DbContext` to attach to; on-by-default there would start the process-wide SqlClient diagnostic unbidden and still require the consumer's own `EnlistCommitCoordination` usage. These stay **explicit opt-in** (the consumer registers `AddPostgreSqlCommitCoordination()` / `AddSqlServerCommitCoordination()` and uses the helpers).
- The **multi-signal-source architecture** (EF interceptor + SqlServer out-of-band diagnostic + Npgsql inline + InMemory coexist). No exactly-one-provider gate is introduced (origin D6/D-series).
- **Per-call publish ergonomics.** `producer.PublishAsync(...)` inside the coordinated transaction already auto-enlists on the ambient coordinator. No `DepositPost`/`BeginTransaction(publisher)` equivalent is added.

### Deferred to Follow-Up Work
- Extending on-by-default to the raw-ADO paths (would require a different "intent" signal and accepts the process-wide diagnostic cost).
- A Roslyn analyzer for un-signalled inline-provider enlistment (origin D12 — already deferred).

---

## Requirements

- **R1.** Choosing `UseEntityFramework<TContext>()` for messaging storage enables the transactional outbox by default, with no other consumer calls required: a publish inside a coordinated transaction is atomic with the DB write and discarded on rollback.
- **R2.** The EF commit interceptor is attached to the consumer's `DbContext` automatically, including a plain `AddDbContext<TContext>` registration (no consumer `AddInterceptors`).
- **R3.** A consumer can opt out via a single explicit builder call (`WithoutTransactionalOutbox()`), restoring non-transactional immediate dispatch.
- **R4.** When the outbox is enabled but the interceptor is not firing (mis-wire), startup surfaces it loudly — Warn by default (log + health degrade), Strict on opt-in (throw at startup). The check must not mutate consumer data.
- **R5.** The auto-registration must not regress the multi-signal-source design (no exactly-one gate) and must preserve the unconditional-`AddSingleton` ordering guarantee for `ICurrentCommitCoordinator`.
- **R6.** `AddHeadlessDbContext`/`AddHeadlessIdentityDbContext` attach DI-registered interceptors via the same `IDbContextOptionsConfiguration` mechanism, so the wiring also reaches consumers' own plain `AddDbContext<TContext>`.
- **R7.** No dependency cycle is introduced (every new edge runs Messaging → CommitCoordination).
- **R8.** Docs (`docs/llms/messaging.md`, `docs/llms/commit-coordination.md`, package READMEs) and demos reflect the new default.

---

## Key Technical Decisions

- **KTD-1 — Interceptor attachment via `IDbContextOptionsConfiguration<TContext>`.** Register this interface in DI; EF Core 10 auto-applies it when building options for `TContext`, attaching the singleton commit interceptor to a plain `AddDbContext<TContext>` with no consumer code. Verified empirically (a spike registered the config + a marker interceptor against a plain `AddDbContext`, and the interceptor fired). Interface member: `Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder)`. The commit interceptor is a singleton, so the EF scoped-interceptor limitation (efcore#36866) does not apply.
- **KTD-2 — On-by-default lives in the messaging storage `UseEntityFramework<TContext>` path.** That method already captures the context as a runtime `Type?`; the auto-registration resolves the generic `IDbContextOptionsConfiguration<TContext>` and the validation gate via `MakeGenericType`, exactly as `*EntityValidationStartupGate<TContext>` is registered today. New dependency edges `Headless.Messaging.Storage.{PostgreSql,SqlServer} → Headless.CommitCoordination.EntityFramework` — **no cycle** (Messaging already depends on `CommitCoordination.Abstractions`; all edges run Messaging → CommitCoordination).
- **KTD-3 — Opt-out method: `WithoutTransactionalOutbox()`** on the messaging setup builder. Reads naturally opting out of an on-by-default behavior. Order-independent: it sets a builder flag the storage extension reads when it composes registrations.
- **KTD-4 — Gate default mode: Warn (Strict opt-in),** reusing the `SqlServerCommitDiagnosticProbe` `Disabled/Warn/Strict` enum shape (origin D11). Consistent with the framework's established probe posture; correctness never depends on the signal (the relay recovers), so a recoverable mis-wire should not hard-block boot by default.
- **KTD-5 — Probe shape: live throwaway coordinated transaction, rolled back.** The gate opens a transaction on the consumer's `DbContext`, enlists commit coordination, asserts the commit signal is observed, then **rolls back** — never touching consumer data (mirrors how `SqlServerCommitDiagnosticProbe` commits a no-op empty transaction). This proves the interceptor truly fires end-to-end, not merely that it is attached.
- **KTD-6 — The `AddHeadlessDbContext` interceptor refactor lands in the same PR (#428).** The mechanism is already proven and the outbox feature needs it regardless; doing both keeps the interceptor-wiring story coherent. `AddDiRegisteredInterceptors(sp)` is retained as a public helper for plain-`AddDbContext` consumers who don't go through the auto-registration path, but `AddHeadlessDbContext` switches to the `IDbContextOptionsConfiguration` registration so its wiring also reaches consumers' own `AddDbContext<TContext>`.

---

## High-Level Technical Design

Registration + startup flow for `AddHeadlessMessaging(setup => setup.UseEntityFramework<AppDbContext>())`:

```mermaid
flowchart TD
    A["setup.UseEntityFramework&lt;AppDbContext&gt;()"] --> B{WithoutTransactionalOutbox()<br/>called?}
    B -- "yes (opt-out)" --> Z["MessagingNullCommitCoordinator only<br/>(non-transactional immediate dispatch)"]
    B -- "no (default)" --> C["Storage extension AddServices:<br/>AddCommitCoordination()<br/>+ AddEntityFrameworkCommitCoordination()<br/>+ register IDbContextOptionsConfiguration&lt;AppDbContext&gt;<br/>+ register CommitInterceptorStartupGate&lt;AppDbContext&gt;"]
    C --> D["Consumer's AddDbContext&lt;AppDbContext&gt;<br/>(plain, no AddInterceptors)"]
    D --> E["EF Core applies IDbContextOptionsConfiguration<br/>→ commit interceptor attached to options"]
    C --> F["Host StartingAsync:<br/>CommitInterceptorStartupGate probe"]
    F --> G{"throwaway coordinated tx:<br/>did commit signal fire?"}
    G -- "yes" --> H["OK — outbox is atomic"]
    G -- "no (mis-wire)" --> I{Mode}
    I -- "Warn (default)" --> J["log loud + degrade health"]
    I -- "Strict" --> K["throw → host fails to start"]
```

Runtime invariant (unchanged): `producer.PublishAsync` inside a coordinated transaction reads `ICurrentCommitCoordinator.Current`, captures the relational transaction via `IRelationalCommitContext`, writes the outbox row in that transaction, and drains on the commit signal.

---

## Implementation Units

### U1. EF commit-interceptor auto-attach via `IDbContextOptionsConfiguration<TContext>`

**Goal:** Provide a DI-registered configuration that attaches the commit-coordination interceptor to any `DbContext<TContext>` whose options EF Core builds — including a plain `AddDbContext<TContext>`.
**Requirements:** R2, R6.
**Dependencies:** none.
**Files:**
- `src/Headless.CommitCoordination.EntityFramework/CommitCoordinationOptionsConfiguration.cs` (new) — `internal sealed class CommitCoordinationOptionsConfiguration<TContext>(IEnumerable<IInterceptor> interceptors) : IDbContextOptionsConfiguration<TContext> where TContext : DbContext`; `Configure` adds the DI-registered commit interceptor(s) to the builder, deduped by reference against any already on `CoreOptionsExtension.Interceptors` (reuse the dedup logic shape from `AddDiRegisteredInterceptors`).
- `src/Headless.CommitCoordination.EntityFramework/Setup.cs` (modify) — add a registration helper that, given a `DbContext` runtime `Type`, registers `IDbContextOptionsConfiguration<TContext>` via `MakeGenericType` + `TryAddEnumerable` (EF Core allows multiple configurations per context; dedup keeps it safe). Keep the existing non-generic `AddEntityFrameworkCommitCoordination()` for the signal-source registration; add the generic options-configuration registration as a separate, composable call.
- `tests/Headless.CommitCoordination.EntityFramework.Tests.Unit/CommitCoordinationOptionsConfigurationTests.cs` (new).

**Approach:** The config resolves the commit interceptor from DI (it is registered as `IInterceptor` singleton by `AddEntityFrameworkCommitCoordination`). Scope what it attaches to the commit-coordination interceptor specifically (not arbitrary DI interceptors) to avoid surprising attachment of unrelated interceptors registered for other reasons. Decide at implementation: attach only the `CommitCoordinationTransactionInterceptor`, resolved by concrete type, rather than all `IInterceptor`s.

**Patterns to follow:** `src/Headless.Orm.EntityFramework/SetupOptionsExtension.cs` (`AddDiRegisteredInterceptors` dedup-by-reference); `*EntityValidationStartupGate<TContext>` generic-by-`Type` registration in `src/Headless.Settings.Storage.EntityFramework/Setup.cs`.

**Test suite design:** Unit tests in the existing `Headless.CommitCoordination.EntityFramework.Tests.Unit` project (SQLite, in-memory). No new harness.

**Test scenarios:**
- Registering `IDbContextOptionsConfiguration<TContext>` + a plain `AddDbContext<TContext>` (no `AddInterceptors`) attaches the commit interceptor — assert the interceptor is present on the resolved context's options / fires on `SaveChanges`. (This is the behavior the spike proved; lock it as a regression test.)
- Dedup: if the consumer ALSO added the interceptor in their own options action, it is not attached twice (assert single instance / fires once).
- The configuration attaches only the commit interceptor, not unrelated DI-registered `IInterceptor`s.

**Verification:** New unit tests pass; a plain `AddDbContext<TContext>` with only the config registered observes the commit interceptor firing.

### U2. On-by-default registration in the messaging EF-context storage path

**Goal:** When `UseEntityFramework<TContext>()` is chosen and the outbox is not opted out, auto-register commit coordination + the EF signal source + the U1 options-configuration so the outbox is atomic with zero consumer wiring.
**Requirements:** R1, R5, R7.
**Dependencies:** U1, U3 (the opt-out flag must be readable).
**Files:**
- `src/Headless.Messaging.Storage.PostgreSql/Setup.cs` (modify) — in the EF-context branch of the `{Provider}MessagesOptionsExtension.AddServices`, when transactional outbox is enabled, call `AddCommitCoordination()` + `AddEntityFrameworkCommitCoordination()` + the U1 generic options-configuration registration (resolving `DbContextType`).
- `src/Headless.Messaging.Storage.SqlServer/Setup.cs` (modify) — same.
- `src/Headless.Messaging.Storage.PostgreSql/Headless.Messaging.Storage.PostgreSql.csproj` + `.../SqlServer/*.csproj` (modify) — add `ProjectReference` to `Headless.CommitCoordination.EntityFramework`.
- `tests/Headless.Messaging.Storage.PostgreSql.Tests.Unit` + `.SqlServer.Tests.Unit` (modify/new) — registration-shape tests.

**Approach:** Reuse the existing `IMessagesOptionsExtension.AddServices` hook (the post-storage registration seam). Only the EF-context branch (`DbContextType != null`) triggers the auto-registration; raw-ADO branches are untouched. Preserve the unconditional-`AddSingleton` ordering guarantee — `AddCommitCoordination` already registers `ICurrentCommitCoordinator` via `AddSingleton` (not `TryAdd`), so it wins over the messaging null fallback regardless of order (R5).

**Patterns to follow:** existing `{Provider}MessagesOptionsExtension.AddServices`; `src/Headless.CommitCoordination.Core/Setup.cs` (ordering guarantee).

**Test suite design:** Unit (registration-shape) in the storage `.Tests.Unit` projects; end-to-end atomicity in the conformance harness (U6).

**Test scenarios:**
- After `AddHeadlessMessaging(setup => setup.UseEntityFramework<TContext>())`, the container resolves a real `ICurrentCommitCoordinator` (not the null fallback) and the EF signal source.
- The `IDbContextOptionsConfiguration<TContext>` is registered for the captured context type.
- Raw-ADO path (`UsePostgreSql(connString)` without a context) does NOT auto-register coordination (stays opt-in).
- Registration-order independence: `AddHeadlessMessaging` before/after any consumer `AddCommitCoordination` still yields the real coordinator.

**Verification:** Registration tests pass; no dependency cycle (solution builds); raw-ADO path unaffected.

### U3. Opt-out API — `WithoutTransactionalOutbox()`

**Goal:** Let a consumer deliberately keep non-transactional immediate dispatch on the EF-context path.
**Requirements:** R3.
**Dependencies:** none (but U2 reads the flag).
**Files:**
- `src/Headless.Messaging.Core/Configuration/MessagingSetupBuilder.cs` (modify) — add `WithoutTransactionalOutbox()` setting an internal flag; expose the flag to storage extensions (internal accessor, same visibility path the storage packages already use to reach `setup.Services`).
- `tests/Headless.Messaging.Core.Tests.Unit/MessagingBuilderTests.cs` (modify).

**Approach:** Order-independent flag on the builder, read by U2's storage-extension `AddServices`. When set, the storage extension skips the coordination auto-registration (leaving the `MessagingNullCommitCoordinator` fallback) and skips the startup gate (U4).

**Test scenarios:**
- `setup.UseEntityFramework<TContext>().WithoutTransactionalOutbox()` → no real coordinator registered (`Current` resolves null), no gate registered.
- Flag works regardless of call order relative to `UseEntityFramework`.

**Verification:** Opt-out test proves non-transactional path is restored and the gate does not run.

### U4. Startup validation gate — commit-interceptor self-probe

**Goal:** Fail loud at startup when the outbox is on but the commit interceptor is not firing for the consumer's `DbContext`.
**Requirements:** R4.
**Dependencies:** U1, U2.
**Files:**
- `src/Headless.CommitCoordination.EntityFramework/CommitInterceptorStartupGate.cs` (new) — `internal sealed class CommitInterceptorStartupGate<TContext> : IHostedLifecycleService where TContext : DbContext`; `StartingAsync` runs the probe.
- `src/Headless.CommitCoordination.EntityFramework/CommitInterceptorProbeMode.cs` (new) — `Disabled / Warn / Strict` enum (mirror `SqlServerCommitDiagnosticProbeMode`).
- Options carrier for the mode (new or fold into an existing EF commit-coordination options type); default `Warn`.
- `src/Headless.Messaging.Storage.{PostgreSql,SqlServer}/Setup.cs` (modify) — register the gate via `TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IHostedService), typeof(CommitInterceptorStartupGate<>).MakeGenericType(dbContextType)))` when the outbox is on.
- `tests/Headless.CommitCoordination.EntityFramework.Tests.Unit/CommitInterceptorStartupGateTests.cs` (new).

**Approach:** In `StartingAsync`, resolve the `DbContext` via `IDbContextFactory<TContext>` (or a scope), open a transaction, `EnlistCommitCoordination`, perform a trivial no-op unit, and assert the commit signal is observed via the signal source — then **roll back**. Never commit consumer data. On failure: Warn → log loud + mark health degraded (reuse the probe-state pattern); Strict → throw `InvalidOperationException` with an instructive message ("commit coordination is enabled for `<TContext>` but the interceptor did not fire — ensure the DbContext is registered through AddHeadlessDbContext or that AddInterceptors/AddDiRegisteredInterceptors wired it").

**CRITICAL constraint:** the probe must not mutate consumer data — assert on a rolled-back transaction (observe the rollback edge or a synthetic commit signal on a throwaway unit). Settle the exact "observe signal without committing" technique at implementation; the `SqlServerCommitDiagnosticProbe` no-op-empty-transaction approach is the reference.

**Patterns to follow:** `src/Headless.Settings.Storage.EntityFramework/Internal/SettingsEntityValidationStartupGate.cs` (IHostedLifecycleService + generic-by-Type registration + instructive throw); `src/Headless.CommitCoordination.SqlServer/SqlServerCommitDiagnosticProbe.cs` + `SqlServerCommitDiagnosticHostedService.cs` (mode enum, Warn/Strict handling, no-op probe).

**Test suite design:** Unit tests with SQLite — a correctly-wired context (gate passes) and a deliberately-unwired context (gate Warns by default, throws in Strict). Integration coverage folds into U6.

**Test scenarios:**
- Correctly-wired `DbContext` (interceptor attached via U1): gate passes, no log/throw.
- Deliberately-unwired `DbContext` (interceptor NOT attached): Warn mode logs + degrades health, does NOT throw; Strict mode throws `InvalidOperationException` with the instructive message at startup.
- The probe rolls back — assert no row is persisted by the probe (no consumer-data mutation).
- Opt-out (U3): gate is not registered, so a non-transactional setup does not trip it.

**Verification:** Gate tests pass; the unwired-context test proves the footgun is now loud, not silent.

### U5. Refactor `AddHeadlessDbContext` / `AddHeadlessIdentityDbContext` onto `IDbContextOptionsConfiguration`

**Goal:** Wire DI-registered interceptors via the same auto-applied mechanism so they also reach consumers' own plain `AddDbContext<TContext>`, not only the framework's `AddDbContext` lambda.
**Requirements:** R6.
**Dependencies:** U1.
**Files:**
- `src/Headless.Orm.EntityFramework/SetupEntityFramework.cs` (modify) — register a generic `IDbContextOptionsConfiguration<TContext>` that runs `AddDiRegisteredInterceptors`-equivalent attachment, instead of (or in addition to) the in-lambda `optionsBuilder.AddDiRegisteredInterceptors(serviceProvider)` seam at line ~80.
- `src/Headless.Orm.EntityFramework/SetupOptionsExtension.cs` (modify) — keep `AddDiRegisteredInterceptors` public (still useful for consumers wiring their own options action) but ensure no double-attach when both the config and the lambda run.
- `src/Headless.Identity.Storage.EntityFramework/Setup.cs` (modify) — same refactor.
- `tests/Headless.Orm.EntityFramework.Tests.Integration/` (modify) — extend the existing interceptor-wiring regression to also cover a consumer's plain `AddDbContext<TContext>`.

**Approach:** A general (non-commit-specific) `IDbContextOptionsConfiguration<TContext>` that attaches all DI-registered `IInterceptor`s (dedup by reference) — superseding the lambda seam. Decide at implementation whether to remove the lambda call entirely or keep it as belt-and-suspenders (prefer removing to avoid two mechanisms; dedup makes either safe). This is the generalized form of U1; consider sharing one implementation parameterized by "which interceptors."

**Patterns to follow:** U1; existing `AddDiRegisteredInterceptors`.

**Test scenarios:**
- `AddHeadlessDbContext<TContext>` still attaches DI interceptors (existing regression, now via the config).
- A consumer's **own** `AddDbContext<TContext>` (not `AddHeadlessDbContext`) with `AddHeadlessDbContextServices` present also receives the interceptors — the new capability.
- No double-attach when both paths are present.

**Verification:** Existing ORM interceptor-wiring tests pass unchanged; the new plain-`AddDbContext` coverage passes.

### U6. Cross-provider conformance + atomicity/regression tests

**Goal:** Prove end-to-end that on-by-default is atomic across EF providers and that mis-wire fails loud.
**Requirements:** R1, R4 (end-to-end).
**Dependencies:** U2, U4.
**Files:**
- `tests/Headless.Messaging.*` integration projects (PostgreSql + SqlServer, Testcontainers) — new tests asserting: with only `AddHeadlessMessaging(setup => setup.UseEntityFramework<TContext>())` + a plain `AddDbContext<TContext>`, a publish inside a coordinated transaction is atomic (commit → message stored+dispatched; rollback → neither row nor message survives).
- A regression that a deliberately-unwired `DbContext` trips the startup gate (Strict throws; Warn logs).
- The opt-out path yields non-transactional behavior.

**Approach:** Prefer the existing commit-coordination conformance harness shape (`tests/Headless.CommitCoordination.Tests.Harness`) where it fits; add messaging-storage-level integration where the outbox row + dispatch must be observed. Needs Docker for the container providers — build-verify + run where Docker is available; state plainly if a provider leg is build-only in a given environment.

**Test scenarios:**
- EF/PostgreSql on-by-default: commit → outbox row persisted + dispatched; rollback → discarded. (Covers R1.)
- EF/SqlServer on-by-default: same.
- Unwired context + Strict → host start throws; + Warn → logs and starts. (Covers R4.)
- Opt-out → publish is non-transactional immediate dispatch.

**Verification:** Conformance tests green on available providers; mis-wire regression proves the loud failure.

### U7. Docs sync + demo simplification

**Goal:** Reflect the new default in agent-facing docs and simplify the demos that currently hand-wire coordination.
**Requirements:** R8.
**Dependencies:** U2, U3, U4.
**Files:**
- `docs/llms/messaging.md` (modify) — document on-by-default transactional outbox for the EF path, the opt-out, and the startup gate.
- `docs/llms/commit-coordination.md` (modify) — cross-reference the messaging auto-wiring; document `IDbContextOptionsConfiguration`-based interceptor attachment.
- `src/Headless.Messaging.Core/README.md`, `src/Headless.CommitCoordination.EntityFramework/README.md` (modify) — keep llms/README in lockstep per `docs/authoring/AUTHORING.md`.
- `docs/plans/2026-06-08-002-...md` (modify) — add a decision entry (D14) recording on-by-default + the gate.
- `demo/Headless.Messaging.RabbitMq.SqlServer.Demo` + `demo/Headless.Messaging.Kafka.PostgreSql.Demo` (modify) — the EF-path `/coordinated/ef`, `/coordinated/rollback`, `/coordinated/delay` endpoints no longer need manual `AddEntityFrameworkCommitCoordination()` + `AddInterceptors(...)`. Simplify Program.cs to show on-by-default; keep `AddSqlServerCommitCoordination()`/`AddPostgreSqlCommitCoordination()` only where the raw-ADO `/coordinated/adonet` endpoint still needs the provider signal source (note this nuance in the demo).

**Approach:** Follow `docs/authoring/AUTHORING.md` drift checks. The demo simplification doubles as a real-world validation that DX improved.

**Test expectation:** none — docs/demos. (Demos must still compile; verified by the solution build.)

**Verification:** Docs lockstep check passes; demos build; demo Program.cs is visibly simpler for the EF path.

---

## Test Strategy

| Layer | Owner | Coverage |
|---|---|---|
| Unit | `Headless.CommitCoordination.EntityFramework.Tests.Unit` | U1 options-config attach/dedup; U4 gate pass/Warn/Strict/rollback |
| Unit | `Headless.Messaging.{Storage.PostgreSql,Storage.SqlServer}.Tests.Unit` | U2 registration shape; raw-ADO untouched |
| Unit | `Headless.Messaging.Core.Tests.Unit` | U3 opt-out flag |
| Unit | `Headless.Orm.EntityFramework.Tests.Integration` | U5 plain-`AddDbContext` interceptor reach |
| Integration (Docker) | messaging storage integration projects + conformance harness | U6 end-to-end atomicity (PG + SqlServer), mis-wire loud, opt-out non-transactional |

The two load-bearing regressions: **(a)** on-by-default + plain `AddDbContext` is atomic with zero wiring; **(b)** a deliberately-unwired `DbContext` makes the gate fail loud (silent footgun is gone).

---

## Risks & Dependencies

- **R-1 — `IDbContextOptionsConfiguration` behavior on edge registrations.** Verified for plain `AddDbContext<TContext>`; confirm it also composes with `AddDbContextPool`/`AddDbContextFactory` paths a consumer might use, or document the limitation. *Mitigation:* U1 tests cover plain `AddDbContext`; add a pooled-context test or document non-support.
- **R-2 — Probe side-effects.** The startup gate opens a real transaction on the consumer's DB at boot. *Mitigation:* always roll back (KTD-5); Disabled mode available; the probe is a no-op unit.
- **R-3 — Behavior change (default flip).** Existing consumers on the EF path who relied on non-transactional immediate dispatch get transactional semantics. *Mitigation:* greenfield (no deployed consumers); opt-out provided; startup gate makes the new behavior observable, not silent.
- **R-4 — Demo raw-ADO nuance.** The demos mix EF-path storage (now on-by-default) with a raw-ADO coordinated endpoint that still needs the provider signal source. *Mitigation:* U7 keeps the provider registration for the raw endpoint and documents why.
- **Dependency:** builds on the commit-coordination substrate (origin plan, PR #428). Lands in the same PR.

---

## Sequencing

1. **U1** (options-config mechanism) — foundation; unblocks U2, U4, U5.
2. **U3** (opt-out flag) — small; U2 reads it.
3. **U2** (messaging on-by-default registration) — depends on U1 + U3.
4. **U4** (startup gate) — depends on U1 + U2.
5. **U5** (`AddHeadlessDbContext` refactor) — depends on U1; independent of messaging units, can land in parallel.
6. **U6** (conformance/atomicity/regression tests) — depends on U2 + U4.
7. **U7** (docs + demos) — last; depends on the behavior being final.

---

## Documentation Plan

Per `docs/authoring/AUTHORING.md`: update `docs/llms/messaging.md` + `docs/llms/commit-coordination.md` and the paired package READMEs in lockstep; add decision D14 to the origin architecture plan; simplify the two demos. Trigger: public API surface change (`WithoutTransactionalOutbox()`), consumer-visible default change (outbox on-by-default), and new startup behavior (the gate).
