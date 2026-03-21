---
status: pending
priority: p3
issue_id: "115"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Clarify _GetOpenDuration double-overflow via +Infinity path

## Problem Statement

In _GetOpenDuration, `state.EffectiveOpenDuration.TotalSeconds * (1L << safeLevel)` can produce double.PositiveInfinity at high escalation levels (e.g., 30s * 2^62). Math.Min(double.PositiveInfinity, maxSeconds) returns maxSeconds correctly, so behavior is correct by accident. A reader who doesn't know that double.PositiveInfinity compares as greater-than any finite value will be confused. The intent is to cap at maxSeconds but the path through infinity is invisible.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:629-634
- **Risk:** Correctness is accidental; future readers may misunderstand or introduce bugs
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Cap the intermediate product explicitly: `var scaledSeconds = safeLevel >= 53 ? maxSeconds : Math.Min(TotalSeconds * (1L << safeLevel), maxSeconds);`
- **Pros**: Intent is explicit; no +Infinity path
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add an explicit check that prevents the double overflow, making the cap intent visible in code rather than relying on Math.Min(+∞, x) behavior.

## Acceptance Criteria

- [ ] No double.PositiveInfinity produced in _GetOpenDuration
- [ ] MaxOpenDuration cap applies correctly at all escalation levels

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
