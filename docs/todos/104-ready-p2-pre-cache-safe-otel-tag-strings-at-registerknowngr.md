---
status: ready
priority: p2
issue_id: "104"
tags: ["code-review","performance","otel"]
dependencies: []
---

# Pre-cache safe OTel tag strings at RegisterKnownGroups time

## Problem Statement

_SafeTag in CircuitBreakerMetrics does IReadOnlySet lookup on every OTel observation cycle per group. At 50 groups and 10s intervals, that's 50 set lookups plus TagList+Measurement allocations per scrape.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs:74,80,103
- **Discovered by:** compound-engineering:review:performance-oracle

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Pre-compute safe tag string once per group at RegisterKnownGroups time. Cache in Dictionary<string,string>. _SafeTag becomes O(1) dictionary lookup.

## Acceptance Criteria

- [ ] Safe tag strings cached at registration time
- [ ] No per-observation set lookups

## Notes

Known pattern from docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md — pre-register all known groups at startup.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
