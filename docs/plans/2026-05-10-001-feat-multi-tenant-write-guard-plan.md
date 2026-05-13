---
title: "feat: multi-tenant write guard"
type: feat
status: completed
date: 2026-05-10
origin: docs/brainstorms/2026-05-10-001-multi-tenant-write-guard-requirements.md
issue: https://github.com/xshaheen/headless-framework/issues/234
---

# feat: multi-tenant write guard

## Summary

Extend the existing Headless EF save pipeline with an opt-in tenant write guard, an ambient scoped bypass for intentional host/admin writes, and typed non-PII failures for unsafe tenant-owned writes. The implementation stays inside `Headless.Orm.EntityFramework` and updates the multi-tenancy/ORM docs to describe how the guard composes with existing tenant filters and cross-layer tenant-required failures.

---

## Problem Frame

The origin requirements define the product scope: tenant-owned EF writes should fail at the write boundary instead of silently creating missing data or tenant isolation drift. Planning confirms the right implementation seam is the existing Headless save pipeline, which already runs tenant stamping and entity processing before persistence, audit capture, and message publishing.

---

## Requirements

- R1. The guard must be opt-in and preserve existing behavior when not enabled *(see origin: docs/brainstorms/2026-05-10-001-multi-tenant-write-guard-requirements.md)*.
- R2. When enabled, adding a tenant-owned entity without a resolved tenant must fail before the write is persisted.
- R3. When enabled, adding, modifying, or deleting a tenant-owned entity whose effective tenant does not match the current tenant must fail before the write is persisted.
- R4. Non-tenant-owned entities must not be blocked by this guard.
- R5. The guard must provide an explicit scoped bypass for intentional admin or host-level writes where missing tenant context or cross-tenant mutation is expected.
- R6. The scoped bypass must be local to the active operation and must not relax tenant write protection for unrelated work after the scope exits.
- R7. Bypass usage must be discoverable enough for code review and audits; broad context-level relaxation must not be the primary escape hatch.
- R8. Missing-tenant failures must reuse `Headless.Abstractions.MissingTenantContextException`.
- R9. Cross-tenant mutation failures must be typed or otherwise reliably distinguishable from generic persistence failures.
- R10. Failure diagnostics must not include live entity property values or other PII-bearing snapshots.
- R11. Diagnostics may include non-sensitive context such as entity type, failure category, current tenant availability, and entity tenant availability/match status.
- R12. Documentation must explain how to enable the guard, when it rejects writes, how to use the scoped bypass, and how this relates to existing tenant-required behavior in other layers.
- R13. Tests must cover missing-tenant create, matching-tenant create/update/delete, cross-tenant update/delete rejection, non-tenant entity pass-through, disabled-by-default behavior, and scoped bypass behavior.

**Origin actors:** A1 application developer, A2 tenant-scoped runtime path, A3 admin or host-level operation, A4 downstream planner/implementer.
**Origin flows:** F1 guarded tenant write, F2 intentional admin bypass.
**Origin acceptance examples:** AE1 through AE7 are carried forward through U3/U4 test scenarios and U5 documentation coverage.

---

## Scope Boundaries

- Do not turn the guard on by default in this change.
- Do not relax tenant query filters or change `IgnoreMultiTenancyFilter()`.
- Do not change tenant resolution middleware.
- Do not change Mediator or messaging tenant enforcement beyond documentation links.
- Do not make broad DbContext-level relaxation the primary bypass model.
- Do not port `zad-ngo` implementation code directly.
- Do not add database migrations or schema changes.
- Do not include live entity value snapshots in diagnostics.

### Defense Layers and Known Gaps

`IMultiTenant` reads, `IQueryable<T>.ExecuteUpdate(...)`, and `IQueryable<T>.ExecuteDelete(...)` are already covered by the framework's global query filter, wired by `HeadlessDbContextRuntime._ConfigureQueryFilters` and registered under the constant `HeadlessQueryFilters.MultiTenancyFilter` (string value `"MultiTenantFilter"`). The filter is part of every `IQueryable<T>` against an `IMultiTenant` set, so bulk operations that consume that `IQueryable<T>` inherit the tenant predicate by default. The per-query opt-out is `IgnoreMultiTenancyFilter()`, which audit-logs the bypass.

The opt-in `SaveChanges` write guard added by this plan is the second defense layer. It operates on EF's `ChangeTracker` and catches `Add` / `Update` / `Remove` / tracked-property-mutation paths before persistence.

Two paths remain explicitly out of scope for this change:

- **Attach-then-modify.** An attacker-controlled `Attach` populates `OriginalValue` from caller-supplied state, so the in-memory guard's `OriginalValue == currentTenantId` check passes for a row that actually belongs to another tenant. The global query filter does not cover this path because the attacker never queries the row. A SQL-level concurrency-style `WHERE TenantId = @currentTenantId` predicate on the SaveChanges-generated UPDATE/DELETE is the planned follow-up — tracked in the security follow-up issue.
- **Raw SQL** (`DbContext.Database.ExecuteSql(...)`, `ExecuteSqlInterpolated(...)`, `ExecuteSqlRaw(...)`, stored procedures, triggers). Consumers calling raw SQL against `IMultiTenant` tables must include their own `WHERE TenantId = @currentTenantId` predicate or wrap the call in `ITenantWriteGuardBypass.BeginBypass()` for intentional, audited maintenance.

### Deferred to Follow-Up Work

- Default-on strict tenant writes: separate compatibility decision after consumers have an opt-in path.
- Default HTTP mapping for cross-tenant mutation failures: consider after the typed exception shape lands and downstream usage is clear.
- Removing `zad-ngo` local guard copies: separate PR in `xshaheen/zad-ngo` after the framework API ships.

---

## Context & Research

### Relevant Code and Patterns

- `src/Headless.Orm.EntityFramework/Contexts/HeadlessEntityModelProcessor.cs` already performs tenant stamping in `_TrySetMultiTenantId(...)` during `ProcessEntries(...)`.
- `src/Headless.Orm.EntityFramework/Contexts/HeadlessSaveChangesRunner.cs` calls `entityProcessor.ProcessEntries(context)` before audit capture, message publishing, transaction creation, and base `SaveChanges`, making it the right fail-fast seam.
- `src/Headless.Orm.EntityFramework/Setup.cs` registers `IHeadlessEntityModelProcessor` and ambient tenant services from `AddHeadlessDbContextServices()`.
- `src/Headless.Core/Abstractions/ICurrentTenant.cs` uses `Change(...)` with an `IDisposable` AsyncLocal-backed scope; the guard bypass should mirror this operation-local pattern.
- `src/Headless.Core/Abstractions/MissingTenantContextException.cs` is already the shared missing-tenant failure type and is already mapped by `Headless.Api`.
- `src/Headless.Mediator/Behaviors/TenantRequiredBehavior.cs` shows the strict tenant boundary pattern: require non-blank current tenant unless an explicit opt-out applies.
- `tests/Headless.Orm.EntityFramework.Tests.Integration/HeadlessDbContextTests.cs` already exercises save behavior and global tenant filters against PostgreSQL Testcontainers.
- `tests/Headless.Orm.EntityFramework.Tests.Integration/Fixture/HeadlessDbContextTestFixture.cs` provides mutable `TestCurrentTenant`, test clock, test user, and `TestHeadlessDbContext`.
- `docs/llms/multi-tenancy.md` already references the EF write guard as a sibling of Mediator and messaging strict tenancy, so the implementation docs should update that placeholder into concrete guidance.

### Institutional Learnings

- `docs/solutions/api/aspnet-core-cancellation-vs-timeout-differentiation-2026-05-07.md` reinforces typed exception mapping discipline: semantically distinct failures should not be swept into generic exception handling, and response bodies must avoid surfacing exception internals.
- `docs/plans/2026-05-03-002-feat-messaging-phase1-foundations-plan.md` established `MissingTenantContextException` as the shared cross-layer missing-tenant guard type, not a messaging-specific exception.
- `docs/plans/2026-05-09-001-feat-publish-filter-tenant-propagation-plan.md` treats the EF write guard, Mediator behavior, and strict publish tenancy as separate enforcement seams that share missing-tenant semantics only.

### External References

- Not used. The implementation is anchored in Headless-owned EF save pipeline behavior rather than external EF interceptor design.

---

## Key Technical Decisions

- **Use the existing Headless EF save pipeline, not a separate interceptor as the primary path.** `ProcessEntries(...)` already centralizes pre-save tenant stamping, auditing inputs, and domain-message discovery. Failing there prevents persistence and prevents local/distributed message publishing from running after an unsafe write.
- **Add ORM-level guard configuration.** API `MultiTenancyOptions` configures HTTP claim resolution and should not grow persistence behavior. The guard gets its own `Headless.Orm.EntityFramework` options and convenience registration surface.
- **A single opt-in guard enables the tenant write invariant.** When enabled, creates require a resolved tenant and all tenant-owned writes must persist under the current tenant unless a scoped bypass is active. Avoid per-state defaults that let consumers accidentally enable only part of the invariant.
- **Use `MissingTenantContextException` only for missing tenant context.** Cross-tenant mutation should use a dedicated typed failure so consumers can distinguish "no tenant was set" from "this write targets another tenant."
- **Model admin escape as an ambient scoped bypass.** This mirrors `ICurrentTenant.Change(...)`, keeps bypass local to the operation, works in API/worker/console hosts, and avoids a broad DbContext-level relaxed mode.
- **Keep diagnostics structural, not value-bearing.** Exception data may name entity type and failure kind, but must not include the entity property bag or current values.
- **Test through real EF save behavior.** Cross-tenant modify/delete depends on EF state and query-filter interactions, so integration coverage is more valuable than isolated unit tests around helper methods.

---

## Open Questions

### Resolved During Planning

- **Where should enforcement run?** In the existing entity-processing/save pipeline, before persistence, audit capture, and message publishing.
- **Should this use HTTP `MultiTenancyOptions`?** No. HTTP tenant resolution and EF write enforcement are separate concerns.
- **How do admin/null-tenant writes stay possible?** Through a scoped bypass, confirmed by the user during brainstorm.
- **Does this need external EF interceptor research?** No for the primary path. The framework owns the save pipeline and already has the right seam.
- **Where should the cross-tenant write exception live?** Originally planned for `Headless.Orm.EntityFramework`. Final decision: `ITenantWriteGuardBypass`, `TenantWriteGuardBypass`, and `CrossTenantWriteException` live in `Headless.Core` under the `Headless.Abstractions` namespace. Keeping the exception in Core lets `HeadlessApiExceptionHandler` (in `Headless.Api`) catch it and map to HTTP 409 with the `g:cross-tenant-write` error descriptor without forcing an `Api → EF` project reference. The EF package consumes the abstractions; HTTP hosts consume the mapping; neither layer needs a hard dependency on the other.

### Deferred to Implementation

- **Original-vs-current tenant comparison details:** Confirm the exact EF metadata/current/original value access needed for tracked, attached, and deleted entries before finalizing the guard helper.
- **Options validation dependency:** If an options validator is added in `Headless.Orm.EntityFramework`, confirm whether the existing `Headless.Hosting` reference is sufficient or whether the project needs an explicit package/project reference for the validator type.

---

## High-Level Technical Design

> *This illustrates the intended approach and is directional guidance for review, not implementation specification. The implementing agent should treat it as context, not code to reproduce.*

The guard evaluates tenant-owned entries before save-side effects run:

| Entry kind | Current tenant | Entity tenant | Bypass | Outcome |
| --- | --- | --- | --- | --- |
| Added | missing | any value | off | Missing-tenant failure |
| Added | tenant A | missing | off | Stamp tenant A and allow |
| Added | tenant A | tenant A | off | Allow |
| Added | tenant A | tenant B | off | Cross-tenant failure |
| Modified or deleted | tenant A | tenant A | off | Allow |
| Modified or deleted | tenant A | tenant B or missing | off | Cross-tenant failure |
| Tenant-owned write | any | any | on | Allow, then restore guard when scope exits |
| Non-tenant entity | any | n/a | any | Allow |

Guard placement:

```text
HeadlessDbContext.SaveChanges*
  -> HeadlessSaveChangesRunner
      -> IHeadlessEntityModelProcessor.ProcessEntries
          -> tenant write guard
          -> existing id/audit/concurrency/message processing
      -> audit capture
      -> local messages / base SaveChanges / distributed messages
```

---

## Implementation Units

### U1. Guard configuration and scoped bypass surface

**Goal:** Add the opt-in guard configuration and an operation-local bypass primitive.

**Requirements:** R1, R5, R6, R7

**Dependencies:** None

**Files:**
- Create: `src/Headless.Orm.EntityFramework/SetupEntityFrameworkTenancy.cs` (later co-located with `SetupEntityFramework.cs`) for `TenantWriteGuardOptions`.
- Create (final placement): `src/Headless.Core/Abstractions/ITenantWriteGuardBypass.cs`, `src/Headless.Core/Abstractions/TenantWriteGuardBypass.cs`, and `src/Headless.Core/Abstractions/CrossTenantWriteException.cs` under the `Headless.Abstractions` namespace. The bypass and exception live in Core (not in `Headless.Orm.EntityFramework`) so `HeadlessApiExceptionHandler` (in `Headless.Api`) can catch the exception and map it to HTTP 409 without forcing an `Api → EF` project reference.
- Modify: `src/Headless.Orm.EntityFramework/SetupEntityFramework.cs` to register the bypass and options.
- Modify: `src/Headless.Orm.EntityFramework/Headless.Orm.EntityFramework.csproj` if the options validator requires a direct dependency.
- Test: `tests/Headless.Orm.EntityFramework.Tests.Integration/HeadlessTenantWriteGuardTests.cs`

**Approach:**
- Add an options surface that is disabled by default and can be enabled from the ORM package setup.
- Register the options and bypass services from `AddHeadlessDbContextServices()` so they are present for every `HeadlessDbContext`.
- Add a public convenience registration for enabling the guard, while keeping direct options configuration possible for advanced hosts.
- Implement the bypass with an AsyncLocal-backed scope and disposable restore semantics, mirroring `ICurrentTenant.Change(...)`.

**Execution note:** Test bypass scope behavior before wiring it into entity processing; leakage here would make admin escape unsafe.

**Patterns to follow:**
- `src/Headless.Api/MultiTenancySetup.cs` for option class + validator placement.
- `src/Headless.Core/Abstractions/ICurrentTenant.cs` for disposable AsyncLocal scope semantics.
- `src/Headless.Messaging.Core/MultiTenancy/MultiTenancyMessagingBuilderExtensions.cs` for idempotent tenant-related registration style.

**Test scenarios:**
- Happy path: guard options default to disabled when only `AddHeadlessDbContextServices()` is used.
- Happy path: enabling helper flips guard behavior for contexts resolved afterward.
- Happy path: entering a bypass scope reports bypass active inside the scope and inactive after disposal.
- Edge case: nested bypass scopes restore the previous state in LIFO order.
- Edge case: bypass state does not leak between unrelated async flows.

**Verification:**
- Service-provider construction succeeds with default options and with guard enabled.
- Bypass tests prove operation-local scope behavior before U3 consumes it.

---

### U2. Typed cross-tenant failure surface

**Goal:** Add a distinguishable, non-PII failure for cross-tenant tenant-owned writes.

**Requirements:** R8, R9, R10, R11

**Dependencies:** None

**Files:**
- Create (final placement): `src/Headless.Core/Abstractions/CrossTenantWriteException.cs` under the `Headless.Abstractions` namespace, not in `Headless.Orm.EntityFramework`. Placing the typed failure in Core enables `HeadlessApiExceptionHandler` (in `Headless.Api`) to map it to HTTP 409 with the `g:cross-tenant-write` error descriptor without forcing an `Api → EF` project reference.
- Test: `tests/Headless.Orm.EntityFramework.Tests.Integration/HeadlessTenantWriteGuardTests.cs`

**Approach:**
- Keep `MissingTenantContextException` as the only failure for missing ambient tenant context.
- Add a dedicated cross-tenant write exception with safe structural diagnostics only.
- Do not add default HTTP mapping for the new cross-tenant exception in this unit; consumers can catch the typed failure and map it while the framework gains real usage data.

**Patterns to follow:**
- `src/Headless.Core/Abstractions/MissingTenantContextException.cs` for shared guard exception style.
- `docs/llms/api.md` information-disclosure invariant: exception `Message`, `Data`, and internals should not leak into ProblemDetails bodies.

**Test scenarios:**
- Happy path: missing-tenant create still throws `MissingTenantContextException`.
- Error path: cross-tenant modify/delete throws the new typed failure, not `MissingTenantContextException`.
- Error path: exception diagnostics expose entity type/failure category only, not the changed entity property values.

**Verification:**
- Consumers can catch missing-tenant and cross-tenant failures separately.
- No test asserts or implementation path requires logging entity property bags.

---

### U3. Entity processor guard enforcement

**Goal:** Enforce tenant-owned write invariants inside the existing Headless entity-processing pipeline.

**Requirements:** R2, R3, R4, R5, R6, R8, R9, R10, R11

**Dependencies:** U1, U2

**Files:**
- Modify: `src/Headless.Orm.EntityFramework/Contexts/HeadlessEntityModelProcessor.cs`
- Modify: `src/Headless.Orm.EntityFramework/Setup.cs`
- Test: `tests/Headless.Orm.EntityFramework.Tests.Integration/HeadlessTenantWriteGuardTests.cs`

**Approach:**
- Inject guard options and bypass state into `HeadlessEntityModelProcessor`.
- If the guard is disabled or bypass is active, preserve current behavior.
- For tenant-owned `Added` entries, require a non-blank current tenant, stamp when the entity tenant is empty, and reject mismatches when the entity already carries a different tenant.
- For tenant-owned `Modified` and `Deleted` entries, compare effective entity tenant ownership against the current tenant and reject mismatches before audit/message side effects run.
- Leave non-tenant entities untouched.

**Execution note:** Characterize current default behavior first so the opt-in guard cannot accidentally change existing tests.

**Patterns to follow:**
- Existing `_TrySetMultiTenantId(...)` behavior for tenant stamping.
- Existing switch in `_ProcessEntry(...)` for state-specific processing.
- `ObjectPropertiesHelper.TrySetProperty(...)` for setting init/private tenant properties when stamping.

**Test scenarios:**
- Happy path: guard disabled, adding tenant-owned entity with no current tenant preserves today's behavior.
- Happy path: guard enabled, current tenant set, added tenant-owned entity without tenant is stamped and saved under current tenant.
- Happy path: guard enabled, current tenant set, matching-tenant update succeeds.
- Happy path: guard enabled, current tenant set, matching-tenant physical delete succeeds.
- Happy path: guard enabled, non-tenant entity add/update/delete succeeds regardless of current tenant.
- Error path: guard enabled, tenant-owned add without current tenant fails before persistence.
- Error path: guard enabled, tenant-owned add with explicit different tenant fails before persistence.
- Error path: guard enabled, tenant-owned update loaded through ignored tenant filter under a different current tenant fails before persistence.
- Error path: guard enabled, tenant-owned physical delete loaded through ignored tenant filter under a different current tenant fails before persistence.
- Edge case: guard enabled, soft-delete-style modified entity owned by a different tenant fails as a modified write.
- Edge case: guard enabled, scoped bypass active, intentional no-tenant and cross-tenant writes may persist.

**Verification:**
- Existing `HeadlessDbContextTests` behavior remains green with the default disabled guard.
- New guard tests fail before U3 and pass after U3.

---

### U4. Integration test fixture and coverage hardening

**Goal:** Build focused integration coverage that proves the guard through real EF state transitions without making existing save tests brittle.

**Requirements:** R13

**Dependencies:** U1, U2, U3

**Files:**
- Create: `tests/Headless.Orm.EntityFramework.Tests.Integration/HeadlessTenantWriteGuardTests.cs`
- Create: `tests/Headless.Orm.EntityFramework.Tests.Integration/Fixture/TenantWriteGuardDbContextTestFixture.cs`

**Approach:**
- Prefer a guard-specific fixture or service-provider setup so existing tests keep the default disabled guard.
- Seed cross-tenant rows through matching tenant scope or scoped bypass, then load through `IgnoreMultiTenancyFilter()` to exercise real admin-style cross-tenant attempts.
- Include both async and sync save only if implementation introduces divergent paths; otherwise one async integration suite is enough because both paths share `ProcessEntries(...)`.

**Patterns to follow:**
- `tests/Headless.Orm.EntityFramework.Tests.Integration/HeadlessDbContextTests.cs` for PostgreSQL-backed save tests.
- `tests/Headless.Orm.EntityFramework.Tests.Integration/Fixture/HeadlessDbContextTestFixture.cs` for tenant/user/clock setup.

**Test scenarios:**
- Integration: disabled guard does not reject current fixture behavior.
- Integration: enabled guard rejects unsafe writes before `SaveChangesAsync(...)` persists rows.
- Integration: bypass scope allows the specific admin operation and then restores strict behavior.
- Integration: rejection happens before local/distributed messages are emitted for unsafe entries.
- Edge case: update/delete tests prove query-filter bypass alone is insufficient; write bypass must also be present.

**Verification:**
- Targeted integration suite for `Headless.Orm.EntityFramework.Tests.Integration` passes.
- The new tests provide direct coverage for each origin acceptance example that affects implementation.

---

### U5. Documentation updates

**Goal:** Document the guard as part of the multi-tenancy and ORM contract.

**Requirements:** R12

**Dependencies:** U1, U2, U3

**Files:**
- Modify: `docs/llms/multi-tenancy.md`
- Modify: `docs/llms/orm.md`
- Modify: `src/Headless.Orm.EntityFramework/README.md`

**Approach:**
- Replace forward-looking references to the EF write guard with concrete enablement and behavior guidance.
- Explain the difference between query-filter bypass and write-guard bypass: `IgnoreMultiTenancyFilter()` affects reads only; guarded cross-tenant writes still require scoped write bypass.
- Document missing-tenant create, cross-tenant modify/delete, non-tenant pass-through, disabled-by-default compatibility, and scoped admin/host bypass.
- Link the missing-tenant behavior to the existing API ProblemDetails mapping, and state that cross-tenant mutation has its own typed failure.

**Patterns to follow:**
- `docs/llms/multi-tenancy.md` sections for Mediator-boundary enforcement and strict publish tenancy.
- `docs/llms/messaging.md` strict tenancy wording for disabled-by-default compatibility.
- Package README style in `src/Headless.Orm.EntityFramework/README.md`.

**Test scenarios:**
- Test expectation: none -- documentation-only unit, verified by review and by matching the implemented public API names after U1-U3.

**Verification:**
- Docs name the correct setup entry points and bypass concept.
- Docs do not imply the guard is default-on.
- Docs do not suggest using broad DbContext relaxation for admin paths.

---

## System-Wide Impact

- **Interaction graph:** `HeadlessDbContext.SaveChanges*` and any `HeadlessIdentityDbContext` path that uses `HeadlessSaveChangesRunner` will see the guard when enabled through shared services.
- **Error propagation:** Missing tenant context uses `MissingTenantContextException` and existing API mapping. Cross-tenant mutation uses a dedicated typed failure; default HTTP mapping is deferred.
- **State lifecycle risks:** Guard failures must occur before audit capture, domain message publishing, and base persistence so unsafe writes have no side effects.
- **API surface parity:** The guard needs a setup/option surface and a bypass surface; no changes to query-filter APIs, Mediator APIs, or messaging APIs.
- **Integration coverage:** Unit tests alone cannot prove attached/deleted EF states, query-filter bypass interactions, or no-persistence behavior; PostgreSQL integration tests are required.
- **Unchanged invariants:** Tenant query filters still determine read visibility. The write guard is an additional save-time invariant, not a replacement for filters.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Bypass scope leaks across async work and weakens protection | Implement with AsyncLocal restore semantics and cover nested/unrelated async flows in tests |
| Cross-tenant comparison uses the wrong EF value for attached or deleted entries | Centralize tenant value resolution and test tracked, ignored-filter-loaded, and deleted states |
| Guard runs after audit/message side effects | Keep enforcement in `ProcessEntries(...)` before existing audit/message pipeline work |
| Existing consumers are surprised by new behavior | Guard is disabled by default and docs call out opt-in compatibility |
| `IgnoreMultiTenancyFilter()` users assume read bypass also permits writes | Docs and tests explicitly show read-filter bypass is not enough; write bypass is required |
| Cross-tenant exception lacks default HTTP mapping | Defer framework mapping intentionally; typed exception allows explicit consumer handling until semantics settle |

---

## Documentation / Operational Notes

- Existing applications remain unchanged until they enable the guard.
- Admin/host maintenance code that writes without tenant context should wrap only the intended operation in the scoped bypass.
- Teams enabling the guard should monitor `MissingTenantContextException` and the new cross-tenant failure during rollout; both indicate either a missing tenant context or an intentional admin path missing its bypass.
- After implementation lands, capture a `docs/solutions/` learning for the guard-vs-query-filter distinction and scoped bypass pattern if code review surfaces reusable lessons.

---

## Sources & References

- **Origin document:** [docs/brainstorms/2026-05-10-001-multi-tenant-write-guard-requirements.md](../brainstorms/2026-05-10-001-multi-tenant-write-guard-requirements.md)
- **Issue:** [xshaheen/headless-framework#234](https://github.com/xshaheen/headless-framework/issues/234)
- Related code: `src/Headless.Orm.EntityFramework/Contexts/HeadlessEntityModelProcessor.cs`
- Related code: `src/Headless.Orm.EntityFramework/Contexts/HeadlessSaveChangesRunner.cs`
- Related code: `src/Headless.Core/Abstractions/MissingTenantContextException.cs`
- Related docs: `docs/llms/multi-tenancy.md`
- Related plan: `docs/plans/2026-05-03-002-feat-messaging-phase1-foundations-plan.md`
- Related plan: `docs/plans/2026-05-09-001-feat-publish-filter-tenant-propagation-plan.md`
