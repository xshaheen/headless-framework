---
status: done
priority: p3
issue_id: "015"
tags: ["code-review","messaging","observability","performance","dotnet"]
dependencies: []
---

# OTel metric tag uses new KeyValuePair allocation — use TagList struct instead

## Problem Statement

CircuitBreakerMetrics.RecordTrip and RecordOpenDuration use new KeyValuePair<string,object?> which heap-allocates on every call. TagList is a stack-allocated struct for up to 8 tags, avoiding this allocation.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs:RecordTrip and RecordOpenDuration
- **Discovered by:** performance-oracle

## Proposed Solutions

### Use TagList { { 'messaging.consumer.group', groupName } } instead of new KeyValuePair
- **Pros**: Zero allocation, idiomatic OTel .NET
- **Cons**: None
- **Effort**: Tiny
- **Risk**: Low


## Recommended Action

Replace new KeyValuePair<string,object?> with var tags = new TagList { { 'messaging.consumer.group', groupName } }; and pass tags.

## Acceptance Criteria

- [ ] No heap allocation in RecordTrip or RecordOpenDuration

## Notes

PR #194 review.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-20 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
