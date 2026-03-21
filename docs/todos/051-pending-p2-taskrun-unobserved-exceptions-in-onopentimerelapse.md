---
status: pending
priority: p2
issue_id: "051"
tags: ["code-review","dotnet"]
dependencies: []
---

# Task.Run unobserved exceptions in _OnOpenTimerElapsed — stuck circuit

## Problem Statement

_OnOpenTimerElapsed uses _ = Task.Run(async () => ...) discarding the task. If _ReopenAfterResumeFailureAsync throws between lock release and pauseCallback invocation (e.g., NRE in metrics.RecordTrip), the exception is unobserved. Circuit ends up in HalfOpen permanently: re-open partially succeeded but pause callback never invoked — transport still runs against an Open circuit.

## Findings

- **Location:** CircuitBreakerStateManager.cs:471-483
- **Risk:** High — stuck-open circuit with consumers still running
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add .ContinueWith(t => logger.LogError(t.Exception, ...), OnlyOnFaulted) to Task.Run. Better: restructure _ReopenAfterResumeFailureAsync to not throw between state changes and callbacks.

## Acceptance Criteria

- [ ] Unobserved exceptions from Task.Run are logged
- [ ] Circuit cannot be stuck in partially-open state

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
