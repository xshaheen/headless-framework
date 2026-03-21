---
status: pending
priority: p3
issue_id: "113"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Cap EscalationLevel to prevent unbounded growth and double-increment in _ReopenAfterResumeFailureAsync

## Problem Statement

_ReopenAfterResumeFailureAsync increments state.EscalationLevel outside the normal _TransitionToOpen path (line 612-615), creating a double-increment on failed resume: once in _TransitionToOpen + once in _ReopenAfterResumeFailureAsync. Under persistent resume failures, the escalation counter grows at 2x the normal rate. Additionally, EscalationLevel has no upper bound — it can theoretically overflow int.MaxValue on a permanently unhealthy dependency (albeit after years of sustained failure).

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:439,612-615
- **Risk:** Double-increment on resume failure causes incorrect escalation timing; theoretical int overflow
- **Discovered by:** compound-engineering:review:security-sentinel

## Proposed Solutions

### Cap EscalationLevel at 63 (max safe bit shift); use Math.Min(state.EscalationLevel + 1, 63) at all increment sites
- **Pros**: Bounded, consistent
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Use Math.Min(state.EscalationLevel + 1, 63) at both increment sites. Review whether the _ReopenAfterResumeFailureAsync increment is intentional or should be removed.

## Acceptance Criteria

- [ ] EscalationLevel capped at 63
- [ ] Double-increment on resume failure addressed
- [ ] Escalation timing tests pass

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
