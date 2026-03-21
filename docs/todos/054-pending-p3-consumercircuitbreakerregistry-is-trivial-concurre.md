---
status: pending
priority: p3
issue_id: "054"
tags: ["code-review","architecture"]
dependencies: []
---

# ConsumerCircuitBreakerRegistry is trivial ConcurrentDictionary wrapper

## Problem Statement

ConsumerCircuitBreakerRegistry wraps ConcurrentDictionary<string, ConsumerCircuitBreakerOptions> with thin delegate methods (Register, RegisterOrUpdate, Remove, TryGet). Zero added behavior. Adds a file, a DI registration, and indirection for no gain.

## Findings

- **Location:** ConsumerCircuitBreakerRegistry.cs
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Replace with plain ConcurrentDictionary or inline into CircuitBreakerStateManager.

## Acceptance Criteria

- [ ] Registry class removed or justified

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
