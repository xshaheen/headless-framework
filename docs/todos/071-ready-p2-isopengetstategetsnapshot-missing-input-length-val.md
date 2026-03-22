---
status: ready
priority: p2
issue_id: "071"
tags: ["code-review","security"]
dependencies: []
---

# IsOpen/GetState/GetSnapshot missing input length validation — CPU amplification DoS

## Problem Statement

ICircuitBreakerMonitor.ResetAsync validates `groupName.Length <= 256` and `IsNotNull(groupName)`, but IsOpen, GetState, and GetSnapshot have no validation at all. A caller passing a very long string triggers O(N) hash computation for the ConcurrentDictionary lookup. If these methods are reachable via an HTTP health-check or management API, this is a CPU-amplification DoS vector.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs (IsOpen, GetState, GetSnapshot entry points)
- **Discovered by:** security-sentinel (P2)

## Proposed Solutions

### Add Argument.IsNotNull + Argument.IsLessThanOrEqualTo(groupName.Length, 256) to IsOpen, GetState, GetSnapshot
- **Pros**: Consistent with ResetAsync, closes DoS vector
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Apply the same guards already present on ResetAsync to all groupName-accepting methods on ICircuitBreakerStateManager.

## Acceptance Criteria

- [ ] IsOpen, GetState, GetSnapshot all validate groupName is not null and length <= 256
- [ ] Tests for validation on each method

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
