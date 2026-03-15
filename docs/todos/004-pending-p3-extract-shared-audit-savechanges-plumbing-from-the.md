---
status: pending
priority: p3
issue_id: "004"
tags: ["code-review","quality","dotnet","architecture"]
dependencies: []
---

# Extract shared audit SaveChanges plumbing from the two DbContext base types

## Problem Statement

The audit capture and persistence logic was copy-pasted into both HeadlessDbContext and HeadlessIdentityDbContext. Any future fix to audit ordering, retries, or error handling now has to land twice, which raises the chance that the two save pipelines drift again.

## Findings

- **Location:** src/Headless.Orm.EntityFramework/Contexts/HeadlessDbContext.cs:43-116 and 462-531
- **Location:** src/Headless.Identity.Storage.EntityFramework/HeadlessIdentityDbContext.cs:61-122 and 453-530
- **Risk:** Low - duplicated save-path logic increases maintenance cost and makes correctness fixes easier to miss

## Proposed Solutions

### Extract a shared helper or protected base routine
- **Pros**: Keeps audit behavior aligned across both context types and reduces the patch surface
- **Cons**: Requires some refactoring in already-sensitive SaveChanges code
- **Effort**: Medium
- **Risk**: Low

### Leave duplicated code but add parity tests
- **Pros**: Lower immediate implementation cost
- **Cons**: Does not reduce the maintenance burden or review noise
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Factor the new audit pipeline into a shared helper so both DbContext variants execute the same logic from one place.

## Acceptance Criteria

- [ ] Audit capture/save ordering is implemented once for both base context types
- [ ] Parity tests cover both HeadlessDbContext and HeadlessIdentityDbContext behavior
- [ ] Future audit fixes only require one production-code change

## Notes

Raised from the code-simplicity lens rather than a direct runtime failure.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
