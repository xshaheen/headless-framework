---
status: pending
priority: p2
issue_id: "005"
tags: ["code-review","maintainability","duplication","audit"]
dependencies: []
---

# Duplicate _ResolveAndPersistAudit implementation in HeadlessDbContext and HeadlessIdentityDbContext

## Problem Statement

Both HeadlessDbContext and HeadlessIdentityDbContext contain identical private _ResolveAndPersistAuditAsync and _ResolveAndPersistAudit methods (4-5 lines each). These methods exist because HeadlessIdentityDbContext does NOT inherit from HeadlessDbContext (it inherits from IdentityDbContext). Every future change to the audit persist logic must be applied in two places. With two contexts to maintain, this pattern compounds as more logic is added. The current implementation is already duplicated verbatim.

## Findings

- **HeadlessDbContext._ResolveAndPersistAuditAsync:** src/Headless.Orm.EntityFramework/Contexts/HeadlessDbContext.cs:459-471
- **HeadlessDbContext._ResolveAndPersistAudit:** src/Headless.Orm.EntityFramework/Contexts/HeadlessDbContext.cs:473-482
- **HeadlessIdentityDbContext._ResolveAndPersistAuditAsync:** src/Headless.Identity.Storage.EntityFramework/HeadlessIdentityDbContext.cs:460-474
- **HeadlessIdentityDbContext._ResolveAndPersistAudit:** src/Headless.Identity.Storage.EntityFramework/HeadlessIdentityDbContext.cs:474-486

## Proposed Solutions

### Move to AuditSavePipelineHelper as ResolveAndPersist(DbContext, Func<bool, Task<int>>, ...) overloads
- **Pros**: Single source of truth. Future changes apply once.
- **Cons**: Requires passing _BaseSaveChanges delegate through AuditSavePipelineHelper, slightly indirect.
- **Effort**: Small
- **Risk**: Low

### Accept the duplication as inherent to the two-context hierarchy
- **Pros**: No refactoring risk.
- **Cons**: Maintenance burden grows.
- **Effort**: None
- **Risk**: Low


## Recommended Action

Move the resolve+persist logic to AuditSavePipelineHelper static methods that accept a Func<bool, CancellationToken, Task<int>> for the async base save, and a Func<bool, int> for the sync base save. Both contexts can call the shared static methods with their respective _BaseSaveChanges delegates.

## Acceptance Criteria

- [ ] _ResolveAndPersistAudit logic exists in exactly one place
- [ ] Both HeadlessDbContext and HeadlessIdentityDbContext use the same implementation
- [ ] All existing tests pass

## Notes

Discovered during PR #187 review. Minor quality issue, not blocking.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
