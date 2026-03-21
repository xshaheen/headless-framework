---
status: pending
priority: p1
issue_id: "040"
tags: ["code-review","dotnet","security","performance"]
dependencies: []
---

# Guard against unbounded OTel metrics cardinality from attacker-controlled groupName

## Problem Statement

CircuitBreakerStateManager._GetOrAddState creates a new GroupCircuitState for every distinct groupName it sees. The groupName originates from transport message headers which can be set by broker publishers. If an attacker can publish messages with arbitrary group names, each unique value: (1) allocates a new GroupCircuitState (Timer, SemaphoreSlim, Lock) in _groups and _locks, (2) registers a new dimensional series in OTel Counter/Histogram — OTel SDK caches one MetricPoint per label-set, limited by ViewConfig.CardinalityLimit (default: 2000). Exceeding the limit causes silent metric data loss or OOM. This is a memory exhaustion / DoS vector.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:152-171 - _GetOrAddState
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs:31-41 - unbounded tag cardinality
- **Risk:** High - memory exhaustion + OTel metric loss under attacker-controlled input
- **Discovered by:** compound-engineering:review:security-sentinel

## Proposed Solutions

### Validate groupName against known groups before _GetOrAddState
- **Pros**: Eliminates unbounded growth, unknown groups are logged and dropped
- **Cons**: Requires access to ConsumerRegistry in CircuitBreakerStateManager
- **Effort**: Medium
- **Risk**: Low

### Cap OTel cardinality with sentinel label for unknown groups
- **Pros**: Partial mitigation, simpler
- **Cons**: State still allocated for unknown groups
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

In _GetOrAddState, check the groupName against the set of consumer groups registered at startup. If unknown, log a warning and return a disabled/no-op state rather than creating real state. Also cap OTel label cardinality by substituting _unknown for unrecognized group names in CircuitBreakerMetrics.

## Acceptance Criteria

- [ ] Attacker-controlled groupNames do not cause unbounded _groups growth
- [ ] OTel metrics do not grow unbounded with arbitrary group names
- [ ] Unknown group names are logged as warnings
- [ ] Legitimate consumer groups still work correctly

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
