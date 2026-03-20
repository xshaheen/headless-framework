---
status: done
priority: p2
issue_id: "009"
tags: ["code-review","messaging","circuit-breaker","dotnet"]
dependencies: []
---

# onResume exception in _OnOpenTimerElapsed swallowed silently — circuit stuck in HalfOpen

## Problem Statement

In CircuitBreakerStateManager._OnOpenTimerElapsed, the circuit transitions to HalfOpen state (inside the lock), then fires the resumeCallback in a fire-and-forget Task.Run with all exceptions swallowed. If _ResumeGroupAsync throws (e.g. broker reconnect fails), the circuit is stuck in HalfOpen with no consumers running and no observable signal. TryAcquireProbePermit returns false for subsequent callers, ReportSuccess/ReportFailure are never called, and the state machine is permanently frozen.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:_OnOpenTimerElapsed
- **Risk:** Permanent HalfOpen state freeze if resume callback throws
- **Discovered by:** strict-dotnet-reviewer, security-sentinel, performance-oracle, architecture-strategist, agent-native-reviewer

## Proposed Solutions

### Log exception at Error and transition back to Open
- **Pros**: Recoverable
- **Cons**: Slightly more code
- **Effort**: Small
- **Risk**: Low

### Log exception at Error (minimum viable)
- **Pros**: Immediate fix, operator can observe and restart
- **Cons**: Doesn't auto-recover
- **Effort**: Tiny
- **Risk**: Low


## Recommended Action

At minimum: log the exception at Error with groupName context. Ideal: transition back to Open with a fresh timer so the circuit retries HalfOpen automatically.

## Acceptance Criteria

- [ ] resumeCallback exception is logged at Error level with group name
- [ ] System is observable when this failure occurs
- [ ] Optionally: circuit auto-recovers to Open for retry

## Notes

PR #194 review.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-20 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
