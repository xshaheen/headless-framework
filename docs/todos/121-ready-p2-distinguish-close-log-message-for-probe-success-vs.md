---
status: ready
priority: p2
issue_id: "121"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Distinguish close log message for probe success vs poison message in circuit breaker

## Problem Statement

_TransitionToClosed is called from ReportSuccess (genuine recovery) and from ReportFailureAsync on HalfOpen+non-transient (poison message closed the circuit). Both emit 'Circuit breaker HalfOpen → Closed for group {Group}'. An operator at 3am cannot distinguish service recovery from a bad message triggering the close — actively misleading in incident response.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:473-501
- **Risk:** Misleading log — poison message close looks identical to service recovery
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer

## Proposed Solutions

### Add reason parameter to _TransitionToClosed or log separately at each call site
- **Pros**: Operators can distinguish recovery from poison-message bypass
- **Cons**: Minor refactor
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Log 'HalfOpen → Closed (probe succeeded)' at Information from ReportSuccess, and 'HalfOpen → Closed (non-transient failure, dependency healthy)' at Warning from the poison-message path.

## Acceptance Criteria

- [ ] Log distinguishes probe success from non-transient failure close
- [ ] ReportSuccess close at Information level
- [ ] Non-transient close at Warning level

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
