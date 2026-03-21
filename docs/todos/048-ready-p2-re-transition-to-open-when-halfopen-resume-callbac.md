---
status: ready
priority: p2
issue_id: "048"
tags: ["code-review","dotnet","quality","security"]
dependencies: []
---

# Re-transition to Open when HalfOpen resume callback fails

## Problem Statement

In CircuitBreakerStateManager._OnOpenTimerElapsed, when the timer fires, the state transitions to HalfOpen and resumeCallback() is called in a fire-and-forget Task.Run. If resumeCallback fails (e.g., transport throws during ResumeAsync), the error is only logged. The circuit stays HalfOpen indefinitely: no new timer is scheduled, no re-open occurs, and no messages are processed. An attacker who can cause a transient error on the resume path (timed network partition) can permanently halt a consumer group with no self-healing.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs - _OnOpenTimerElapsed, Task.Run resume callback
- **Risk:** High - permanent consumer group halt with no self-healing
- **Discovered by:** compound-engineering:review:security-sentinel

## Proposed Solutions

### Re-transition to Open on resume callback failure
- **Pros**: Self-healing: a new timer will schedule the next HalfOpen attempt
- **Cons**: Requires calling _TransitionToOpen from the catch block
- **Effort**: Small
- **Risk**: Low

### Emit metric/alert and document manual override
- **Pros**: Simpler, visible to operators
- **Cons**: No self-healing, requires operator intervention
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

In the catch block of the resume Task.Run, re-transition the circuit to Open (re-enter _TransitionToOpen under the group lock) to schedule a new timer. This ensures the circuit eventually retries the HalfOpen probe.

## Acceptance Criteria

- [ ] Resume callback failure triggers re-transition to Open
- [ ] New timer scheduled after failed resume
- [ ] Error is logged before re-transition
- [ ] Test covers stuck-HalfOpen scenario

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
