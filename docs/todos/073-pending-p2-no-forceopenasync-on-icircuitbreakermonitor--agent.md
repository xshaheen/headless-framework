---
status: pending
priority: p2
issue_id: "073"
tags: ["code-review","architecture"]
dependencies: []
---

# No ForceOpenAsync on ICircuitBreakerMonitor — agents cannot proactively pause consumer groups

## Problem Statement

ICircuitBreakerMonitor provides ResetAsync (force-close) but no way to force a circuit open. An operator or agent responding to a known downstream outage — 'I know the payment service is down, pause the payments consumer now before failures accumulate' — has no API to express that intent. The only workaround is waiting for natural tripping (N consecutive failures after the outage starts) or restarting with changed config.

## Findings

- **Missing method:** ICircuitBreakerMonitor.ForceOpenAsync(string groupName, CancellationToken ct)
- **Discovered by:** agent-native-reviewer (P1 — kept at P2 since it is additive, not a bug)

## Proposed Solutions

### Add ValueTask<bool> ForceOpenAsync(string groupName, CancellationToken ct = default) to ICircuitBreakerMonitor
- **Pros**: Complete agent-native control surface, mirrors ResetAsync semantics
- **Cons**: Adds API surface
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Add ForceOpenAsync. Implementation in CircuitBreakerStateManager should transition to Open, invoke onPause callback, create the open timer, and record a trip metric. Consider whether forced opens should increment EscalationLevel or reset it.

## Acceptance Criteria

- [ ] ForceOpenAsync added to ICircuitBreakerMonitor and ICircuitBreakerStateManager
- [ ] Implementation transitions circuit to Open state
- [ ] Invokes pause callback
- [ ] OTel trip metric recorded with appropriate tag to distinguish forced from natural trips
- [ ] Unit tests: force open from Closed, force open from HalfOpen
- [ ] XML docs

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
