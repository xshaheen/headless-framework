---
status: pending
priority: p2
issue_id: "111"
tags: ["code-review","performance","thread-safety"]
dependencies: []
---

# Dispose OpenTimer outside lock in _TransitionToOpen

## Problem Statement

_TransitionToOpen disposes state.OpenTimer inside the lock, which is inconsistent with all other disposal sites (ReportFailureAsync, ResetAsync, ForceOpenAsync, DisposeAsync) that dispose outside the lock. Timer.Dispose() may internally acquire TimerQueue lock, creating a lock-ordering dependency.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:710
- **Discovered by:** compound-engineering:review:performance-oracle

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Return old timer from _TransitionToOpen and dispose it outside the lock, matching existing pattern.

## Acceptance Criteria

- [ ] Timer disposal in _TransitionToOpen happens outside the lock
- [ ] Pattern consistent with other disposal sites

## Notes

Source: Code review

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
