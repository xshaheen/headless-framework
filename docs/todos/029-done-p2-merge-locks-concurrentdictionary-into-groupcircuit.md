---
status: done
priority: p2
issue_id: "029"
tags: ["code-review","dotnet","performance","architecture"]
dependencies: []
---

# Merge _locks ConcurrentDictionary into GroupCircuitState

## Problem Statement

CircuitBreakerStateManager maintains two separate ConcurrentDictionary instances: _groups (state) and _locks (Lock per group). Every method does two dictionary lookups per call. In _GetOrAddState, both must be populated atomically but GetOrAdd factory runs unlocked, creating a race window. RemoveGroup has a window between the two TryRemove calls. The comment says locks are kept separate to avoid locking on a property accessor — but a readonly Lock field on GroupCircuitState is not a property accessor.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:26-29
- **Risk:** Medium - double lookup overhead + RemoveGroup race window
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer, performance-oracle, code-simplicity-reviewer

## Proposed Solutions

### Add Lock SyncLock field to GroupCircuitState, drop _locks dictionary
- **Pros**: Single dict lookup, no race in RemoveGroup, co-located lock+state for cache locality
- **Cons**: All callers change from _locks[key] to state.SyncLock
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add public readonly Lock SyncLock = new() to GroupCircuitState. Remove _locks ConcurrentDictionary. Update all lock(groupLock) calls to lock(state.SyncLock). Update _GetOrAddState factory to remove the TryAdd to _locks.

## Acceptance Criteria

- [ ] _locks dictionary removed
- [ ] SyncLock field on GroupCircuitState
- [ ] All callers use state.SyncLock
- [ ] RemoveGroup no longer has two-step race

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
