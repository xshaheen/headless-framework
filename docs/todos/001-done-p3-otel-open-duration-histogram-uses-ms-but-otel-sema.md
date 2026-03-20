---
status: done
priority: p3
issue_id: "001"
tags: ["code-review","messaging","observability","dotnet"]
dependencies: []
---

# OTel open duration histogram uses ms but OTel semantic conventions use s

## Problem Statement

CircuitBreakerMetrics registers the open duration histogram with unit: 'ms'. OpenTelemetry semantic conventions for messaging use seconds ('s') as the canonical time unit for durations. Dashboards that follow OTel conventions will display wrong scale.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Change unit to 's' and record openMs/1000.0
- **Pros**: OTel compliant
- **Cons**: Minor conversion
- **Effort**: Tiny
- **Risk**: Low


## Recommended Action

Change unit to 's' and divide openMs by 1000.0 before recording.

## Acceptance Criteria

- [ ] Histogram unit is 's'
- [ ] Duration value matches in seconds

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
