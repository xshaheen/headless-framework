---
status: done
priority: p3
issue_id: "008"
tags: ["code-review","messaging","dotnet","api-design"]
dependencies: []
---

# RetryProcessorOptions.MaxPollingInterval uses int (seconds) while other options use TimeSpan

## Problem Statement

RetryProcessorOptions.MaxPollingInterval is int (seconds) while CircuitBreakerOptions uses TimeSpan for all time values. This inconsistency will generate 'is that seconds or milliseconds?' questions. MessagingOptions.FailedRetryInterval is also int for consistency with existing API, but MaxPollingInterval is a new property that could use TimeSpan.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/RetryProcessorOptions.cs:MaxPollingInterval
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Change to TimeSpan MaxPollingInterval { get; set; } = TimeSpan.FromMinutes(15)
- **Pros**: Type-safe, self-documenting
- **Cons**: Breaking change if already in use
- **Effort**: Tiny
- **Risk**: Low (new API)


## Recommended Action

Change MaxPollingInterval to TimeSpan since it is a new API not yet published. Update usage in _AdjustPollingInterval accordingly.

## Acceptance Criteria

- [ ] MaxPollingInterval is TimeSpan
- [ ] Usage updated consistently

## Notes

PR #194 review. Low priority since this is a new API.

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
