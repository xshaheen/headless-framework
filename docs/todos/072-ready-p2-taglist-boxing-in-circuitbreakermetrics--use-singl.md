---
status: ready
priority: p2
issue_id: "072"
tags: ["code-review","performance"]
dependencies: []
---

# TagList boxing in CircuitBreakerMetrics — use single KeyValuePair overload to eliminate allocations

## Problem Statement

CircuitBreakerMetrics.RecordTrip (line 71) and RecordOpenDuration (line 78) create a `new TagList { { key, value } }` which boxes the string value as `object?` inside a KeyValuePair. The Counter<long>.Add and Histogram<double>.Record APIs have overloads that accept a single `KeyValuePair<string, object?>` directly, bypassing TagList entirely. This fires precisely at circuit trip time — a stress moment — when you want metrics recording to be allocation-free.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs:71, 78
- **Discovered by:** performance-oracle (P1 — kept at P2 since it's a perf optimization not a correctness bug)

## Proposed Solutions

### Use Counter.Add(1, new KeyValuePair<string,object?>(key, value)) single-tag overload
- **Pros**: Zero-allocation for single-tag metrics, eliminates boxing
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace `new TagList { { k, v } }` with direct `KeyValuePair<string, object?>` overload in both RecordTrip and RecordOpenDuration.

## Acceptance Criteria

- [ ] No TagList allocation in RecordTrip or RecordOpenDuration
- [ ] Single KeyValuePair overload used
- [ ] No behavioral change to metric values or tags

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
