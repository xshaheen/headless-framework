---
status: ready
priority: p2
issue_id: "050"
tags: ["code-review","architecture"]
dependencies: []
---

# ResetAsync accepts but silently ignores CancellationToken — broken public contract

## Problem Statement

ICircuitBreakerMonitor.ResetAsync accepts `CancellationToken cancellationToken = default` on both the interface (ICircuitBreakerMonitor.cs:121) and implementation (CircuitBreakerStateManager.cs:392). The token is never forwarded to the resumeCallback() invocation or anywhere else in the method. This is a semantic lie: callers who pass a token expecting cooperative cancellation will find it silently swallowed.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:392-446
- **Interface:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs:121
- **Discovered by:** strict-dotnet-reviewer (P2.1), simplicity-reviewer (P2)

## Proposed Solutions

### Remove CancellationToken from ResetAsync (non-cancellable operation)
- **Pros**: Honest API, minimal change
- **Cons**: Breaking change if consumers already pass tokens
- **Effort**: Small
- **Risk**: Low

### Change callback type to Func<CancellationToken, ValueTask> and thread the token
- **Pros**: Full cancellation support
- **Cons**: Larger refactor across all transport callback registrations
- **Effort**: Large
- **Risk**: Medium


## Recommended Action

Remove the parameter unless there is a concrete need. ResetAsync is an operator/agent action that completes quickly; cancellation semantics are unclear (abort reset, leave circuit Open?). Drop the token and document the decision.

## Acceptance Criteria

- [ ] CancellationToken removed from ResetAsync signature OR actually threaded into resumeCallback
- [ ] Interface and implementation in sync
- [ ] XML doc updated to remove misleading cancellability implication

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
