---
status: done
priority: p2
issue_id: "083"
tags: ["code-review","performance","messaging","observability"]
dependencies: []
---

# Switch CircuitBreakerMetrics OTel calls from boxing KeyValuePair to TagList struct

## Problem Statement

CircuitBreakerMetrics.RecordTrip and RecordOpenDuration use new KeyValuePair<string, object?> which boxes the struct when passed through the OTel params-array path. The _ObserveCircuitStates gauge callback allocates N KeyValuePairs per OTel scrape cycle (every 10-60s). With many groups this causes continuous GC pressure. TagList is a stack-allocated struct that avoids boxing in the .NET OTel pipeline.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs:72,78
- **Problem:** KeyValuePair<string, object?> boxes on every metric record and per-scrape gauge observation
- **Impact:** N heap allocations per OTel scrape with N consumer groups
- **Discovered by:** performance-oracle

## Proposed Solutions

### Use TagList struct overload
- **Pros**: Zero-allocation, recommended by .NET OTel docs
- **Cons**: Slightly more verbose syntax
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace `new KeyValuePair<string, object?>` with `new TagList { { GroupTagKey, _SafeTag(groupName) } }` at all metric call sites. Pre-declare `private static readonly string GroupTagKey = "messaging.consumer.group"`.

## Acceptance Criteria

- [ ] All metric call sites use TagList instead of KeyValuePair
- [ ] No boxing allocations in RecordTrip, RecordOpenDuration, or _ObserveCircuitStates
- [ ] Benchmark or test confirms allocation-free path

## Notes

PR #194 code review finding.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-23 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
