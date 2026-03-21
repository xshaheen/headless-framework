---
status: pending
priority: p2
issue_id: "058"
tags: ["code-review","security"]
dependencies: []
---

# IsTransientException predicate without exception guard — masks failures

## Problem Statement

User-supplied IsTransientException lambda executes without try/catch. Slow or throwing predicates (e.g., HTTP checks) propagate up to SubscribeExecutor._SetFailedState unhandled — masks the original failure and prevents circuit breaker state updates. Predicate receives raw exception with full stack trace and Exception.Data.

## Findings

- **Location:** CircuitBreakerStateManager.cs:81
- **Risk:** High — unhandled predicate exception prevents circuit state updates
- **Discovered by:** security-sentinel

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Wrap predicate in try/catch returning false on exception. Log warning with exception type only. Add XML doc warning against slow/throwing predicates.

## Acceptance Criteria

- [ ] Predicate wrapped in try/catch
- [ ] Exception in predicate returns false (non-transient)
- [ ] Warning logged with exception type name only

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
