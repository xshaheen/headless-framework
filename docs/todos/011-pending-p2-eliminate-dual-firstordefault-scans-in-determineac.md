---
status: pending
priority: p2
issue_id: "011"
tags: ["code-review","performance","dotnet"]
dependencies: []
---

# Eliminate dual FirstOrDefault scans in _DetermineAction — fold into main property loop

## Problem Statement

EfAuditChangeCapture._DetermineAction (lines 216, 230) calls `entry.Properties.FirstOrDefault(p => p.Metadata.Name == "IsDeleted")` and `entry.Properties.FirstOrDefault(p => p.Metadata.Name == "IsSuspended")` — two linear O(n) scans over all properties for every Modified entity on every SaveChanges. The main property loop in `_CaptureEntry` (line 136) already iterates the same collection. This is 3 passes where 1 would suffice.

## Findings

- **Location:** src/Headless.AuditLog.EntityFramework/EfAuditChangeCapture.cs:216,230
- **Discovered by:** strict-dotnet-reviewer, performance-oracle

## Proposed Solutions

### Fold soft-delete/suspend detection into main property loop
- **Pros**: Single pass over properties; eliminates LINQ delegate allocations
- **Cons**: Requires refactoring _DetermineAction to accept pre-collected flags
- **Effort**: Small
- **Risk**: Low

### Use entry.Metadata.FindProperty() for O(1) lookup
- **Pros**: O(1) by EF Core internal dictionary; cleaner than LINQ
- **Cons**: Still two separate calls, but non-allocating
- **Effort**: Trivial
- **Risk**: Low


## Recommended Action

Move soft-delete/suspend flag detection into the main property loop and compute action from flags after the loop, eliminating _DetermineAction as a separate pre-scan step.

## Acceptance Criteria

- [ ] entry.Properties iterated exactly once per entity (not 3 times)
- [ ] IsDeleted/IsSuspended transition detection preserved
- [ ] Unit tests for soft_deleted, restored, suspended, unsuspended actions still pass

## Notes

Discovered during PR #187 code review.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
