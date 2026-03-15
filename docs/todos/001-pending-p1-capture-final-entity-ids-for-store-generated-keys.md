---
status: pending
priority: p1
issue_id: "001"
tags: ["code-review","data-integrity","dotnet","entity-framework"]
dependencies: []
---

# Capture final entity ids for store-generated keys

## Problem Statement

The audit pipeline snapshots entity ids before SaveChanges runs. For Added entities with identity/sequence/generated keys, EfAuditChangeCapture reads temporary CurrentValue values and persists those into AuditLogEntry.EntityId. The current tests avoid the issue by forcing ValueGeneratedNever, so the main failure mode is untested.

## Findings

- **Location:** src/Headless.Orm.EntityFramework/Contexts/HeadlessDbContext.cs:43-52
- **Location:** src/Headless.AuditLog.EntityFramework/EfAuditChangeCapture.cs:381-410
- **Evidence:** tests/Headless.AuditLog.EntityFramework.Tests.Integration/Fixture/AuditTestDbContext.cs:18-27 forces manual keys and overrides the audit key mapping, so generated-key inserts are never exercised
- **Risk:** High - created audit rows can point at 0, temporary negative ids, or other pre-save key placeholders

## Proposed Solutions

### Two-phase audit capture
- **Pros**: Preserves old/new value capture before save and patches EntityId after the database generates keys
- **Cons**: Requires refactoring the SaveChanges pipeline to keep entity references until the audit rows are materialized
- **Effort**: Medium
- **Risk**: Low

### Reject unsupported generated-key entities
- **Pros**: Small change and makes the limitation explicit
- **Cons**: Breaks the advertised default path for common EF models and reduces audit coverage
- **Effort**: Small
- **Risk**: High


## Recommended Action

Use a two-phase approach so generated keys are resolved after SaveChanges assigns them but before the surrounding transaction commits.

## Acceptance Criteria

- [ ] Created audit entries store the final primary key for identity or sequence-backed entities
- [ ] Owned entities use the final owner key instead of a temporary placeholder
- [ ] Coverage added for at least one database-generated key scenario

## Notes

Found during manual review of the audit subsystem branch against origin/main.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
