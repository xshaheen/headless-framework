---
status: ready
priority: p2
issue_id: "083"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix MaxTrackedGroups cap TOCTOU race and log spam

## Problem Statement

In CircuitBreakerStateManager._GetOrAddState, the check on Count is not atomic with the subsequent GetOrAdd. Two concurrent threads can both pass the count check at Count=999 and both insert distinct keys, exceeding 1000. Additionally, the LogWarning fires on every single call for any group hitting the cap — at 10k msg/s that is 10k LogWarning allocations per second.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs (_GetOrAddState, ~line 2476)
- **Risk:** DoS mitigation cap bypassable under concurrency; log spam at high throughput
- **Discovered by:** security-sentinel, performance-oracle

## Proposed Solutions

### Use a separate Interlocked counter for tracking group count
- **Pros**: Atomic increment on actual add, log only on first breach
- **Cons**: Slightly more complex
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Track inserted count with Interlocked.Increment. Only log once when cap is first hit (use a _capReached flag). Gate insertion atomically.

## Acceptance Criteria

- [ ] _groups never exceeds MaxTrackedGroups + concurrency factor
- [ ] LogWarning fires at most once per cap breach, not per message
- [ ] Test added for concurrent insertion at cap boundary

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
