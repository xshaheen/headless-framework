---
status: pending
priority: p3
issue_id: "017"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Extract requiresExplicitTransaction condition to private method and remove redundant null checks

## Problem Statement

Two minor code quality issues in HeadlessSaveChangesRunner: (1) The 'requiresExplicitTransaction' condition is copy-pasted identically in both ExecuteAsync (line 45-46) and Execute (line 94-95). (2) _ExecuteWithinCurrentTransactionAsync and _ExecuteWithinCurrentTransaction both have a defensive null-check throw on CurrentTransaction that was already checked by the caller with no await between the guard and the read — the throw is dead code.

## Findings

- **Duplicate condition:** src/Headless.Orm.EntityFramework/Contexts/HeadlessSaveChangesRunner.cs:45-46 and 94-95
- **Redundant null check:** src/Headless.Orm.EntityFramework/Contexts/HeadlessSaveChangesRunner.cs:112-114 and 149-151
- **Discovered by:** code-simplicity-reviewer, strict-dotnet-reviewer

## Proposed Solutions

### Extract RequiresExplicitTransaction private static method; replace ?? throw with direct ! access
- **Pros**: DRY, removes dead code
- **Cons**: None
- **Effort**: XS
- **Risk**: Low


## Recommended Action

Extract to: private static bool RequiresExplicitTransaction(IReadOnlyList<AuditLogEntryData>? auditEntries, ProcessBeforeSaveReport report) => auditEntries is { Count: > 0 } || report.DistributedEmitters.Count > 0 || report.LocalEmitters.Count > 0; Replace null-coalescing throws with Database.CurrentTransaction! (using null-forgiving operator).

## Acceptance Criteria

- [ ] requiresExplicitTransaction condition defined in one place
- [ ] Redundant null-check throws removed from both WithinCurrentTransaction methods

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
