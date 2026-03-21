---
status: pending
priority: p3
issue_id: "036"
tags: ["code-review","dotnet","architecture"]
dependencies: []
---

# Add OTel observable gauge for current circuit state per group

## Problem Statement

CircuitBreakerMetrics only emits a trips counter and open_duration histogram — historical signals. There is no gauge exposing the current live circuit state. External monitoring agents (Prometheus, Grafana) cannot determine which groups are currently open without in-process access to ICircuitBreakerMonitor. A gauge `messaging.circuit_breaker.state` (0=Closed, 1=HalfOpen, 2=Open) per group would enable current-state dashboards and alerts.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs
- **Discovered by:** compound-engineering:review:agent-native-reviewer

## Proposed Solutions

### Add ObservableGauge<int> updated on state transitions
- **Pros**: Live state visible to metrics consumers
- **Cons**: Slightly more OTel surface
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add a RecordState(string groupName, int state) method to CircuitBreakerMetrics and call it from each state transition. Or use an ObservableGauge that pulls from _groups on each metrics collection cycle.

## Acceptance Criteria

- [ ] OTel metric messaging.circuit_breaker.state emitted per group
- [ ] Values: 0=Closed, 1=HalfOpen, 2=Open

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
