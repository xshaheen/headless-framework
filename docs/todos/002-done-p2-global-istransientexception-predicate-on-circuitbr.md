---
status: done
priority: p2
issue_id: "002"
tags: ["code-review","messaging","circuit-breaker","dotnet"]
dependencies: []
---

# Global IsTransientException predicate on CircuitBreakerOptions is never called

## Problem Statement

CircuitBreakerOptions.IsTransientException is a configurable Func<Exception,bool> that replaces CircuitBreakerDefaults.IsTransient globally. However CircuitBreakerStateManager.ReportFailureAsync always calls CircuitBreakerDefaults.IsTransient(exception) directly. The configured predicate is never invoked. Any user who sets options.CircuitBreaker.IsTransientException = ex => ... gets no effect.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:ReportFailureAsync line ~941
- **Risk:** Public API does nothing — custom exception classification silently ignored
- **Discovered by:** strict-dotnet-reviewer, security-sentinel, code-simplicity-reviewer

## Proposed Solutions

### Replace CircuitBreakerDefaults.IsTransient(exception) with _options.IsTransientException(exception)
- **Pros**: One-line fix
- **Cons**: Need try/catch around user delegate
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace hardcoded call with _options.IsTransientException(exception) wrapped in try/catch that logs predicate errors and defaults to false. Lookup order: per-consumer IsTransientException (when P1 per-consumer override todo is fixed) > global IsTransientException.

## Acceptance Criteria

- [ ] options.CircuitBreaker.IsTransientException = myPredicate is actually called
- [ ] Predicate exception is caught, logged at Error, and returns false (safe default)
- [ ] Unit test verifies custom predicate is invoked

## Notes

PR #194 review. Depends on P1 todo for per-consumer overrides to get the full picture.

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
