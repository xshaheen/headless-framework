---
status: ready
priority: p2
issue_id: "096"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix double-Warning logging on HalfOpen transition

## Problem Statement

ConsumerRegister._ResumeGroupAsync logs at LogLevel.Warning for normal HalfOpen transitions. CircuitBreakerStateManager._OnOpenTimerElapsed already logs at Warning for the same transition. This produces double-Warning entries in logs for every HalfOpen event, which is noisy and misleading in production monitoring.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs (_ResumeGroupAsync)
- **Risk:** Double log entries for expected operational events — log noise
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Remove the LogWarning from ConsumerRegister._ResumeGroupAsync (keep the one in StateManager)
- **Pros**: Single source of truth for state transition logs
- **Cons**: Slightly less detail at consumer register layer
- **Effort**: Small
- **Risk**: Low

### Downgrade ConsumerRegister log to LogInformation
- **Pros**: Keeps context at consumer layer without log noise
- **Cons**: HalfOpen is warning-level in StateManager, Information elsewhere — mixed levels
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove the LogWarning from ConsumerRegister._ResumeGroupAsync. The StateManager already logs the state transition.

## Acceptance Criteria

- [ ] HalfOpen transition logged exactly once per event
- [ ] No double-Warning entries in log output

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
