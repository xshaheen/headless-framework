---
status: pending
priority: p1
issue_id: "042"
tags: ["code-review","logic-bug","correctness"]
dependencies: []
---

# Fix double escalation-increment in _ReopenAfterResumeFailureAsync

## Problem Statement

_ReopenAfterResumeFailureAsync (CircuitBreakerStateManager.cs:709,730-734) bumps EscalationLevel twice on the pause-failure error path: once inside _TransitionToOpen (which always increments escalation), and again in a separate lock block at lines 730-734 with Math.Min(state.EscalationLevel + 1, 63). This means a single resume failure escalates the open duration by 4x instead of 2x.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:709,730-734
- **Risk:** High — circuits stay open 4x longer than intended after resume failure
- **Discovered by:** performance-oracle

## Proposed Solutions

### Remove the second escalation bump at lines 730-734
- **Pros**: Simple, correct — _TransitionToOpen already handles escalation
- **Cons**: Need to verify _TransitionToOpen escalation is the intended one
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove the duplicate escalation block at lines 730-734. _TransitionToOpen already increments EscalationLevel. The second bump is a bug.

## Acceptance Criteria

- [ ] EscalationLevel incremented exactly once per open transition
- [ ] Resume failure escalation matches normal failure escalation (2x, not 4x)
- [ ] Unit test: resume failure produces expected escalated duration

## Notes

Discovered by performance-oracle during lock-acquisition analysis.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
