---
status: done
priority: p1
issue_id: "001"
tags: ["code-review","correctness","data-integrity","audit"]
dependencies: []
---

# Audit pipeline non-atomic on common SaveChanges path (no emitters)

## Problem Statement

The two-phase audit pipeline is NOT atomic for the most common case: when no message emitters are present. Entity changes commit first via _BaseSaveChanges, then audit entries are added and committed via a SECOND _BaseSaveChanges call. If the second commit fails (DB error, transient fault), entities are persisted but audit entries are permanently lost. The PR description claims 'Audit entries commit in the same SaveChanges transaction as entity changes' which is only true for the explicit-transaction path (when emitters are registered or CreateExecutionStrategy wraps both). For the majority of apps that only use audit logging without message emitters, atomicity is not guaranteed.

## Findings

- **No-emitter async path:** src/Headless.Orm.EntityFramework/Contexts/HeadlessDbContext.cs:48-51
- **No-emitter sync path:** src/Headless.Orm.EntityFramework/Contexts/HeadlessDbContext.cs:122-123
- **Identity context async:** src/Headless.Identity.Storage.EntityFramework/HeadlessIdentityDbContext.cs:67,78
- **_ResolveAndPersistAudit second save:** src/Headless.Orm.EntityFramework/Contexts/HeadlessDbContext.cs:470,481

## Proposed Solutions

### Wrap no-emitter path in explicit transaction when audit entries exist
- **Pros**: Preserves atomic guarantee. Minimal code change. No behavioral change when audit is disabled.
- **Cons**: Adds transaction overhead for all audited saves. Changes existing non-transaction path.
- **Effort**: Medium
- **Risk**: Medium

### Capture audit entries pre-save, add to context before first SaveChanges
- **Pros**: Truly single-commit. Simpler code path.
- **Cons**: Cannot resolve store-generated EntityIds (the whole reason for two-phase pipeline). Would require fallback to NULL EntityId for Added entities.
- **Effort**: Large
- **Risk**: High

### Update docs to accurately describe non-atomic guarantee; add transactional option
- **Pros**: Minimal risk. Honest about behavior.
- **Cons**: Users relying on atomicity will be surprised.
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Wrap the no-emitter path in an explicit transaction when auditEntries.Count > 0. Pattern: if (auditEntries is { Count: > 0 }) { using var tx = Database.BeginTransaction(); _BaseSaveChanges(); _ResolveAndPersistAudit(auditEntries); tx.Commit(); }. Also update PR description and README to clarify the actual atomicity contract.

## Acceptance Criteria

- [ ] The no-emitter path is atomic (entity + audit in same transaction) when audit entries exist
- [ ] Integration test covering a simulated audit Phase 2 failure confirms entity changes are rolled back
- [ ] Documentation accurately describes atomicity guarantee

## Notes

Discovered during PR #187 review. This affects both HeadlessDbContext and HeadlessIdentityDbContext. The explicit-transaction path (CreateExecutionStrategy) IS atomic because both phase saves are within the same transaction scope.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-15 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-15 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
