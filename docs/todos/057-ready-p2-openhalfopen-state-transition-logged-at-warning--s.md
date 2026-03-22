---
status: ready
priority: p2
issue_id: "057"
tags: ["code-review","quality"]
dependencies: []
---

# Open→HalfOpen state transition logged at Warning — should be Information

## Problem Statement

CircuitBreakerStateManager._OnOpenTimerElapsed logs the Open→HalfOpen transition at LogWarning (line 726). Open→HalfOpen is the EXPECTED recovery path, not an error condition. By contrast, HalfOpen→Closed (probe succeeded, line 684) correctly uses LogInformation. The inconsistency means operators will receive Warning-level noise on every recovery attempt.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:726
- **Discovered by:** strict-dotnet-reviewer (P2.6)

## Proposed Solutions

### Change LogWarning to LogInformation at line 726
- **Pros**: Consistent log levels, reduces operator alert fatigue
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change `logger.LogWarning` to `logger.LogInformation` for the Open→HalfOpen transition.

## Acceptance Criteria

- [ ] Open→HalfOpen logged at Information level
- [ ] Consistent with HalfOpen→Closed logging level

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
