---
date: 2026-05-10
topic: multi-tenant-write-guard
---

# Multi-Tenant Write Guard

## Summary

Add an opt-in EF multi-tenant write guard that rejects unsafe writes for tenant-owned entities: missing tenant context on create, cross-tenant modify/delete, and any other write that would silently violate tenant ownership. Intentional host-level or admin maintenance writes remain possible through an explicit scoped bypass.

---

## Problem Frame

Headless already models tenant ownership as a default mental model: tenant-owned rows are read through tenant-aware filters, ambient tenant context can be restored across request and messaging boundaries, and missing tenant context is treated as a first-class failure in sibling layers.

The current EF write path can still silently persist tenant-owned changes without a resolved tenant or mutate rows that do not belong to the current tenant. These failures surface late as missing data, leakage risk, or hard-to-debug tenant isolation defects instead of failing at the write boundary where the cause is still visible.

Admin and host-level tooling complicate the invariant because some operations are intentionally no-tenant or cross-tenant. Those paths need to stay possible, but their bypass should be deliberate, local to the operation, and visible to reviewers.

---

## Actors

- A1. Application developer: Enables strict tenant write protection for a consuming application.
- A2. Tenant-scoped runtime path: Creates, updates, or deletes tenant-owned data under a resolved ambient tenant.
- A3. Admin or host-level operation: Performs intentional no-tenant or cross-tenant maintenance work.
- A4. Downstream planner/implementer: Turns this requirement into the concrete EF integration, API surface, tests, and docs.

---

## Key Flows

- F1. Guarded tenant write
  - **Trigger:** A tenant-scoped runtime path saves tenant-owned entities.
  - **Actors:** A2
  - **Steps:** The guard evaluates each tenant-owned change, compares the operation against the current tenant context, allows matching-tenant writes, and rejects unsafe writes before persistence completes.
  - **Outcome:** Tenant-owned data cannot be created, modified, or deleted outside the resolved tenant boundary without an explicit bypass.
  - **Covered by:** R1, R2, R3, R4, R6

- F2. Intentional admin bypass
  - **Trigger:** An admin or host-level operation needs to write with no tenant context or across tenant boundaries.
  - **Actors:** A3
  - **Steps:** The operation enters a scoped bypass, performs the specific maintenance write, and exits the bypass so strict protection resumes for subsequent work.
  - **Outcome:** Exceptional host-level work remains possible without weakening protection for unrelated writes.
  - **Covered by:** R6, R7, R8

---

## Requirements

**Tenant write protection**
- R1. The guard must be opt-in and preserve existing behavior when not enabled.
- R2. When enabled, adding a tenant-owned entity without a resolved tenant must fail before the write is persisted.
- R3. When enabled, modifying a tenant-owned entity whose tenant does not match the current tenant must fail before the write is persisted.
- R4. When enabled, deleting a tenant-owned entity whose tenant does not match the current tenant must fail before the write is persisted.
- R5. Non-tenant-owned entities must not be blocked by this guard.

**Bypass behavior**
- R6. The guard must provide an explicit scoped bypass for intentional admin or host-level writes where missing tenant context or cross-tenant mutation is expected.
- R7. The scoped bypass must be local to the active operation and must not relax tenant write protection for unrelated work after the scope exits.
- R8. Bypass usage must be discoverable enough for code review and audits; broad context-level relaxation must not be the primary escape hatch.

**Failure and diagnostics**
- R9. Missing-tenant failures must reuse the framework's existing tenant-required failure model so hosts get consistent handling with other tenant-required surfaces.
- R10. Cross-tenant mutation failures must be typed or otherwise reliably distinguishable from generic persistence failures so hosts can suppress retries and map the failure intentionally.
- R11. Failure diagnostics must not include live entity property values or other PII-bearing snapshots.
- R12. Diagnostics may include non-sensitive context such as the entity type, failure category, and tenant availability/match status.

**Documentation and handoff**
- R13. The multi-tenancy documentation must explain how to enable the guard, when it rejects writes, how to use the scoped bypass, and how this relates to existing tenant-required behavior in other layers.
- R14. Tests must cover missing-tenant create, matching-tenant create/update/delete, cross-tenant update/delete rejection, non-tenant entity pass-through, disabled-by-default behavior, and scoped bypass behavior.

---

## Acceptance Examples

- AE1. **Covers R1, R2, R9.** Given the guard is enabled and no tenant is resolved, when a tenant-owned entity is added and saved, the save fails with the tenant-required failure model before data is persisted.
- AE2. **Covers R2.** Given the guard is enabled and a tenant is resolved, when a tenant-owned entity is added without a tenant value already set, the write succeeds only if the persisted tenant ownership matches the resolved tenant.
- AE3. **Covers R3, R10.** Given the guard is enabled and the current tenant is `tenant-a`, when code modifies a tenant-owned entity belonging to `tenant-b`, the save fails with a distinguishable cross-tenant mutation failure.
- AE4. **Covers R4, R10.** Given the guard is enabled and the current tenant is `tenant-a`, when code deletes a tenant-owned entity belonging to `tenant-b`, the save fails with a distinguishable cross-tenant mutation failure.
- AE5. **Covers R5.** Given the guard is enabled, when code saves a non-tenant-owned entity, the guard does not block the write because no tenant ownership invariant applies.
- AE6. **Covers R6, R7, R8.** Given an admin maintenance operation enters the scoped bypass, when it performs an intentional no-tenant or cross-tenant write, the write may proceed; when the scope exits, a later unsafe write without a bypass is rejected.
- AE7. **Covers R11, R12.** Given a guarded write fails, when the exception or log context is inspected, it contains non-sensitive diagnostic metadata only and does not expose entity property values.

---

## Success Criteria

- Consumers can enable strict EF tenant write protection without breaking existing applications that do not opt in.
- Tenant ownership bugs fail at write time instead of surfacing later as missing data or isolation drift.
- Admin and host-level maintenance remains possible through explicit, reviewable scoped bypasses.
- Planning can proceed without inventing scope: it only needs to decide concrete API shape, EF integration point, failure types, and test placement.

---

## Scope Boundaries

- Do not turn the guard on by default in this change.
- Do not relax tenant query filters or change tenant resolution middleware.
- Do not change Mediator tenant-required behavior or messaging publish tenancy behavior, except for documentation links if helpful.
- Do not make broad DbContext-level relaxation the primary bypass model.
- Do not copy application-specific `zad-ngo` patterns beyond using that work as precedent.
- Do not include live entity value snapshots in diagnostics.

---

## Key Decisions

- Opt-in first: preserves compatibility while giving consumers a safety net they can enable deliberately.
- Guard all unsafe tenant-owned write states, not only creates: cross-tenant modify/delete can violate the same ownership boundary and should fail loudly.
- Scoped bypass for admin/host writes: intentional exceptional work stays possible without weakening the default runtime path.
- Keep diagnostics non-PII: entity values are not safe to attach to exceptions because structured logging can destructure them into sinks.

---

## Dependencies / Assumptions

- The shared tenant-required failure model already exists in the framework and should remain the missing-tenant failure surface.
- The exact guard mechanism, option names, bypass API, and cross-tenant failure type are planning decisions.
- The guard must evaluate effective tenant ownership at save time closely enough to prevent persistence of unsafe changes.
- Consumers that enable the guard are expected to establish tenant context for normal tenant-scoped writes.

---

## Outstanding Questions

### Deferred to Planning

- [Affects R3, R4][Technical] How should the guard reliably determine original tenant ownership for modified and deleted entities across tracked, attached, and shadow-property scenarios?
- [Affects R6, R7][Technical] What scoped bypass API shape best prevents accidental leakage across async flows or nested scopes?
- [Affects R10][Technical] Should cross-tenant mutation use a new typed exception, a specialized tenant exception family, or structured data on the existing failure model?
