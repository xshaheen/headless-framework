---
status: pending
priority: p2
issue_id: "019"
tags: ["code-review","messaging","circuit-breaker","dotnet","api-design"]
dependencies: []
---

# ICircuitBreakerStateManager is internal — circuit state not observable by consumers

## Problem Statement

ICircuitBreakerStateManager is internal, so consumers cannot resolve it from DI to call IsOpen(groupName). The only way to observe circuit state is passive (OTel metrics/logs). Developers building health checks, conditional workflows, or monitoring agents have no queryable API surface. CircuitBreakerState enum is public but unreachable from application code.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerStateManager.cs
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerState.cs
- **Risk:** Programmatic observability gap — cannot query circuit state from application code
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

### Add public ICircuitBreakerMonitor interface with IsOpen/GetState, register CircuitBreakerStateManager as implementing it
- **Pros**: Zero implementation cost, full observability
- **Cons**: Adds public API surface
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Create public ICircuitBreakerMonitor { bool IsOpen(string groupName); CircuitBreakerState GetState(string groupName); } in Headless.Messaging.Core and register CircuitBreakerStateManager as implementing both ICircuitBreakerStateManager (internal) and ICircuitBreakerMonitor (public).

## Acceptance Criteria

- [ ] ICircuitBreakerMonitor is resolvable from DI by application code
- [ ] IsOpen(groupName) returns correct state
- [ ] CircuitBreakerState enum is accessible via the public interface

## Notes

PR #194 review.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
