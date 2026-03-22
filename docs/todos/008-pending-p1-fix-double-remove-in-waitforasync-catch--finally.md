---
status: pending
priority: p1
issue_id: "008"
tags: ["code-review","quality","concurrency"]
dependencies: []
---

# Fix double-remove in WaitForAsync catch + finally

## Problem Statement

In MessageObservationStore.WaitForAsync, _waiters.Remove(entry) is called in BOTH the catch and finally blocks. On the timeout path, this means the entry is removed in catch, then finally acquires _waitersLock again and does a redundant O(n) List.Remove scan. With N concurrent timeouts this becomes O(N²) lock acquisitions. The catch block should not clean up — finally is the sole cleanup site.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessageObservationStore.cs:109-133
- **Impact:** Redundant lock acquisition + linear scan on every timeout; confusing intent
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, performance-oracle, code-simplicity-reviewer (unanimous)

## Proposed Solutions

### Remove cleanup from catch, keep only in finally
- **Pros**: Simplest fix, single cleanup path, standard pattern
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove lock+Remove from catch block. Let finally be the sole cleanup site.

## Acceptance Criteria

- [ ] _waiters.Remove(entry) called exactly once per WaitForAsync invocation
- [ ] finally block is the sole cleanup path
- [ ] Existing tests still pass

## Notes

Flagged by all 5 code review agents. List.Remove is idempotent so this is benign today but the intent is wrong.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
