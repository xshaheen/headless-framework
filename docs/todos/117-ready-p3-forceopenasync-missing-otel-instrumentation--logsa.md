---
status: ready
priority: p3
issue_id: "117"
tags: ["code-review","observability","performance"]
dependencies: []
---

# ForceOpenAsync missing OTel instrumentation + LogSanitizer calls inside lock

## Problem Statement

ForceOpenAsync does not emit OTel metrics when it force-opens a circuit (unlike automatic trips). Separately, _TransitionToOpen/_TransitionToClosed call LogSanitizer.Sanitize inside the lock, allocating in the critical section — inconsistent with ResetAsync/ForceOpenAsync which pre-compute outside the lock.

## Findings

- **ForceOpenAsync OTel:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs
- **Sanitize in lock:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:841
- **Discovered by:** compound-engineering:review:agent-native-reviewer, compound-engineering:review:performance-oracle

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Emit OTel counter/log for ForceOpenAsync. Move LogSanitizer.Sanitize outside lock in state transition methods.

## Acceptance Criteria

- [ ] ForceOpenAsync emits at least a Warning log
- [ ] LogSanitizer.Sanitize calls moved outside lock

## Notes

Source: Code review

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
