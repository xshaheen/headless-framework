---
status: pending
priority: p2
issue_id: "070"
tags: ["code-review","architecture"]
dependencies: []
---

# ICircuitBreakerMonitor.KnownGroups exposes internal startup concern — returns null before RegisterKnownGroups

## Problem Statement

ICircuitBreakerMonitor.KnownGroups is `IReadOnlySet<string>?` and returns null if RegisterKnownGroups (an internal startup hook on ICircuitBreakerStateManager) was not called. This exposes an internal startup sequencing concern on the public interface. External consumers of ICircuitBreakerMonitor have no way to call RegisterKnownGroups themselves and cannot distinguish between 'no groups registered' and 'startup not complete yet'.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs:102-108
- **Discovered by:** simplicity-reviewer (P1 — kept as P2 since it's a design concern not a runtime bug)

## Proposed Solutions

### Move KnownGroups to ICircuitBreakerStateManager (internal interface)
- **Pros**: Cleaner public API surface
- **Cons**: Breaking change if external consumers already use KnownGroups
- **Effort**: Small
- **Risk**: Low

### Keep on ICircuitBreakerMonitor but return empty set instead of null before registration
- **Pros**: Non-breaking, eliminates null footgun
- **Cons**: Cannot distinguish 'not registered yet' from 'registered with 0 groups'
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Return an empty IReadOnlySet<string> instead of null when groups have not been registered yet. Update the XML doc to state that an empty set (not null) is returned before startup completes.

## Acceptance Criteria

- [ ] KnownGroups returns empty set (not null) before RegisterKnownGroups is called
- [ ] XML doc updated to describe the pre-registration behavior
- [ ] No breaking change to existing callers

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
