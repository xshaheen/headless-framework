---
status: done
priority: p2
issue_id: "049"
tags: ["code-review","dotnet","architecture","quality"]
dependencies: []
---

# Add ResetAsync to ICircuitBreakerMonitor for operator/agent-driven circuit recovery

## Problem Statement

ICircuitBreakerMonitor is read-only. Once a circuit opens, neither operators, dashboard users, nor agents can force it closed — they must wait for the exponential backoff timer to elapse (up to MaxOpenDuration = 240s, potentially longer with escalation). This is a critical gap: after a known-fixed dependency (e.g. deployment that fixes a downstream service), operators cannot immediately restore message processing. The ResetAsync capability exists in CircuitBreakerStateManager but it is internal.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs
- **Risk:** High - no recovery path without waiting for timer
- **Discovered by:** compound-engineering:review:agent-native-reviewer

## Proposed Solutions

### Add ResetAsync to ICircuitBreakerMonitor
- **Pros**: Agents and operators can force recovery, dashboard can wire a reset button
- **Cons**: Extends public interface
- **Effort**: Small
- **Risk**: Low

### Create separate ICircuitBreakerControl interface
- **Pros**: Separates read-only observation from control
- **Cons**: More interfaces
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add ValueTask ResetAsync(string groupName, CancellationToken ct = default) to ICircuitBreakerMonitor. Implement in CircuitBreakerStateManager to force-transition to Closed and cancel any open timer. Add dashboard endpoint POST /api/circuit-breakers/{groupName}/reset.

## Acceptance Criteria

- [ ] ICircuitBreakerMonitor.ResetAsync forces circuit to Closed
- [ ] Existing OpenTimer cancelled on reset
- [ ] Dashboard endpoint wires up the reset
- [ ] Test covers force-reset from Open and HalfOpen

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
