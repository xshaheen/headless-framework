---
status: done
priority: p2
issue_id: "048"
tags: ["code-review","security","observability"]
dependencies: []
---

# Apply _SafeTag in OTel observable gauge _ObserveCircuitStates

## Problem Statement

_ObserveCircuitStates (CircuitBreakerMetrics.cs:98-103) emits raw group names as metric tag values, bypassing the _SafeTag cardinality guard. RecordTrip and RecordOpenDuration correctly use _SafeTag. If RegisterKnownGroups is never called, unbounded group names flow into OTel time-series labels causing cardinality explosions.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs:98-103
- **Discovered by:** security-sentinel
- **Known Pattern:** docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md — Pattern 7: OTel cardinality guard

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Replace raw group with _SafeTag(group) in the observable gauge yield. One-line fix.

## Acceptance Criteria

- [ ] _ObserveCircuitStates uses _SafeTag(group) for tag values
- [ ] Consistent cardinality guard across all metric emission paths

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

### 2026-03-22 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
