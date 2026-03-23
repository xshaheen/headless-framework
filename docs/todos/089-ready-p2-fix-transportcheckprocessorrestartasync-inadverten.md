---
status: ready
priority: p2
issue_id: "089"
tags: ["code-review","messaging","correctness","plan-conformance"]
dependencies: []
---

# Fix TransportCheckProcessor.ReStartAsync inadvertently resetting circuit breaker state

## Problem Statement

ConsumerRegister.ReStartAsync calls PulseAsync which calls _circuitBreakerStateManager.RemoveGroup() for every group. This resets all circuit state including escalation level, open timer, and failure counters. The original plan explicitly states 'TransportCheckProcessor.ReStartAsync() does not reset circuit state — orthogonal concerns'. A transport restart (broker reconnect) should not clear the circuit breaker's knowledge of handler failures.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:82,196
- **Problem:** PulseAsync → RemoveGroup clears all circuit state on transport restart
- **Plan criterion:** TransportCheckProcessor.ReStartAsync() does not reset circuit state
- **Discovered by:** plan-conformance-reviewer

## Proposed Solutions

### Remove RemoveGroup call from PulseAsync; use re-register without reset
- **Pros**: Preserves circuit state across transport restarts as designed
- **Cons**: Need to handle group re-registration without clearing state
- **Effort**: Medium
- **Risk**: Medium

### Add a ReRegisterGroup method that preserves state while refreshing callbacks
- **Pros**: Clean separation of concerns
- **Cons**: More code
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Introduce a re-register path in ICircuitBreakerStateManager that refreshes pause/resume callbacks without resetting the state machine. PulseAsync should use re-register, not RemoveGroup+re-add.

## Acceptance Criteria

- [ ] TransportCheckProcessor.ReStartAsync does not clear ConsecutiveFailures
- [ ] TransportCheckProcessor.ReStartAsync does not clear EscalationLevel
- [ ] Circuit state preserved across broker reconnects
- [ ] Unit test: circuit open → transport restart → circuit still open

## Notes

PR #194 plan conformance finding.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
