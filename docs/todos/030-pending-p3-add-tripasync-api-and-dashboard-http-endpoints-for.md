---
status: pending
priority: p3
issue_id: "030"
tags: ["code-review","agent-native","api-design"]
dependencies: []
---

# Add TripAsync API and dashboard HTTP endpoints for circuit breaker

## Problem Statement

ICircuitBreakerMonitor has ResetAsync (close circuit) but no TripAsync (force-open). An agent can recover from incidents but cannot preemptively shed load. Additionally, the dashboard has no HTTP endpoints for circuit breaker state — only in-process DI access. Multi-instance deployments have no remote control surface.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add TripAsync to ICircuitBreakerMonitor. Add GET/POST circuit-breaker endpoints to dashboard. Can be follow-up PR.

## Acceptance Criteria

- [ ] TripAsync method on ICircuitBreakerMonitor
- [ ] Dashboard endpoints for list/reset/trip
- [ ] XML docs for operator/agent use

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
