---
status: done
priority: p1
issue_id: "060"
tags: ["code-review","security","performance"]
dependencies: []
---

# CircuitBreakerMetrics._SafeTag returns raw group name when _knownGroups is null — OTel cardinality DoS

## Problem Statement

CircuitBreakerMetrics._SafeTag returns the raw, attacker-controlled groupName when _knownGroups is null: `return _knownGroups is null || _knownGroups.Contains(groupName) ? groupName : UnknownGroupTag`. When _knownGroups is null (before RegisterKnownGroups is called during startup, or if the library is used without the built-in ConsumerRegister), any distinct group name from broker messages is emitted verbatim as an OTel metric dimension. An attacker sending messages with randomized group headers can create unbounded metric cardinality before startup completes, causing OTel cardinality explosion in the metrics backend.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs:82-87
- **Risk:** OTel cardinality explosion — unbounded metric series creation before startup completes
- **Discovered by:** security-sentinel (P2)
- **Window:** Any failure reports arriving before ExecuteAsync completes bypass the guard

## Proposed Solutions

### Return UnknownGroupTag when _knownGroups is null (safe-by-default)
- **Pros**: One-line fix, eliminates the pre-startup cardinality window
- **Cons**: Trips before startup will show as unknown-group in metrics (acceptable)
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change _SafeTag to: `var known = _knownGroups; if (known is null) return UnknownGroupTag; return known.Contains(groupName) ? groupName : UnknownGroupTag;`

## Acceptance Criteria

- [ ] _SafeTag returns UnknownGroupTag when _knownGroups is null
- [ ] Unit test: RecordTrip before RegisterKnownGroups emits UnknownGroupTag
- [ ] Unit test: RecordTrip after RegisterKnownGroups emits real group name for known groups

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
