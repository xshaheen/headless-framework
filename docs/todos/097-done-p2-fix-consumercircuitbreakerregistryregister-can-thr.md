---
status: done
priority: p2
issue_id: "097"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix ConsumerCircuitBreakerRegistry.Register can throw on duplicate when both config paths used

## Problem Statement

ConsumerCircuitBreakerRegistry.Register throws an exception on duplicate group names, but two configuration paths can produce the same group name: Subscribe<T>().WithCircuitBreaker() and AddConsumer<T,M>().WithCircuitBreaker(). There is no guard preventing a user from using both APIs for the same group. The startup exception message says 'each consumer group can only have one circuit breaker override' but this constraint is not enforced at the API surface.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerRegistry.cs (Register method)
- **Risk:** Confusing startup exception when both config paths used for same group
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Change Register to RegisterOrUpdate with a LogWarning on duplicate
- **Pros**: Graceful handling, last-writer-wins with visibility
- **Cons**: Silently overwrites — could mask misconfiguration
- **Effort**: Small
- **Risk**: Low

### Document constraint in XML docs and log a clear error with guidance
- **Pros**: Fail-fast is correct — better than silent overwrite
- **Cons**: Still throws
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Keep fail-fast but improve the exception message to name the conflicting group and explain which two configuration paths conflicted. Also add XML doc on WithCircuitBreaker() noting the single-registration constraint.

## Acceptance Criteria

- [ ] Exception message names the conflicting group name
- [ ] XML doc on WithCircuitBreaker mentions single-registration constraint
- [ ] Test covers the duplicate registration scenario

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

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
