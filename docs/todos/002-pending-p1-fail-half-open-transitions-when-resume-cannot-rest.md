---
status: pending
priority: p1
issue_id: "002"
tags: ["code-review","dotnet","quality","architecture"]
dependencies: []
---

# Fail half-open transitions when resume cannot restart consumers

## Problem Statement

The timer-driven Open -> HalfOpen transition only re-opens the breaker if the resume callback throws. ConsumerRegister currently catches and logs transport ResumeAsync failures, so the callback returns successfully even when no consumer actually resumes. The breaker can then sit in HalfOpen indefinitely, and retry draining stays blocked until manual reset.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:296-317
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:666-683
- **Risk:** A failed resume can leave a group permanently HalfOpen with retries suppressed
- **Discovered by:** compound-engineering:review pragmatic-dotnet-reviewer

## Proposed Solutions

### Propagate resume failures back to the state manager
- **Pros**: Lets the timer callback reopen and escalate as designed
- **Cons**: Changes existing error-handling behavior in ConsumerRegister
- **Effort**: Small
- **Risk**: Low

### Return explicit per-client resume status
- **Pros**: Makes partial-resume failures observable and testable
- **Cons**: Requires callback and transport contract changes
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Treat resume failure as a real HalfOpen probe failure: bubble it up or aggregate failures so CircuitBreakerStateManager can reopen the circuit automatically.

## Acceptance Criteria

- [ ] A transport resume failure during HalfOpen causes the breaker to reopen instead of remaining HalfOpen
- [ ] Retry suppression clears only after a successful resume/probe cycle or an explicit reopen
- [ ] Unit tests cover resume failure for the timer-driven HalfOpen transition

## Notes

PR #194 code review finding. Prior brainstorm/review notes already stressed deterministic HalfOpen recovery semantics.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
