---
status: pending
priority: p2
issue_id: "124"
tags: ["code-review","dotnet","messaging","performance"]
dependencies: []
---

# Replace ConcurrentDictionary.Count with atomic _groupCount counter in _GetOrAddState hot path

## Problem Statement

CircuitBreakerStateManager._GetOrAddState checks `_groups.Count >= MaxTrackedGroups` on every message delivery when _knownGroups is null. ConcurrentDictionary.Count is O(N) — takes a full lock sweep across all internal segments. At 1000 groups and high throughput this is a global serialization point on the critical message processing path.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:397
- **Risk:** O(N) lock sweep per message at MaxTrackedGroups capacity
- **Discovered by:** compound-engineering:review:performance-oracle, compound-engineering:review:security-sentinel

## Proposed Solutions

### Add private int _groupCount; Interlocked.Increment inside GetOrAdd factory; compare _groupCount instead of _groups.Count
- **Pros**: O(1) check; cap is already documented as approximate
- **Cons**: Factory may run multiple times — slight overcount but acceptable
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Use atomic _groupCount counter. Interlocked.Increment inside value factory gives equivalent approximate-cap semantics.

## Acceptance Criteria

- [ ] MaxTrackedGroups check does not use ConcurrentDictionary.Count
- [ ] Count check is O(1)
- [ ] Cap behavior unchanged (still approximate)

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
