---
status: done
priority: p3
issue_id: "103"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Replace Math.Pow(2, safeLevel) with 1L << safeLevel for exact integer exponentiation

## Problem Statement

CircuitBreakerStateManager._GetOpenDuration uses Math.Pow(2, safeLevel) for exponential backoff calculation. This is floating-point exponentiation where integer bit-shifting (1L << safeLevel) would be exact, faster, and avoids IEEE 754 precision concerns at high escalation levels.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs (_GetOpenDuration, ~line 2699)
- **Risk:** Minor: floating-point rounding at high escalation levels; performance
- **Discovered by:** performance-oracle

## Proposed Solutions

### Replace Math.Pow(2, safeLevel) with (double)(1L << safeLevel)
- **Pros**: Exact integer arithmetic, slightly faster
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change `state.EffectiveOpenDuration.TotalSeconds * Math.Pow(2, safeLevel)` to `state.EffectiveOpenDuration.TotalSeconds * (1L << safeLevel)`.

## Acceptance Criteria

- [ ] Math.Pow replaced with integer bit shift
- [ ] Same escalation behavior verified by existing tests

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
