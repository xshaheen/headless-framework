---
status: ready
priority: p3
issue_id: "030"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Fix RecordOpenDuration parameter name: durationMs but histogram unit is seconds

## Problem Statement

CircuitBreakerMetrics.RecordOpenDuration(string groupName, double durationMs) takes milliseconds but the OTel histogram is registered with unit: 's' (seconds) and the method divides by 1000.0 internally. The parameter name durationMs forces callers to know about the internal conversion. Rename parameter to durationSeconds and remove the /1000 division, or take TimeSpan directly.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs:37-40
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Accept TimeSpan duration parameter
- **Pros**: Type-safe, no unit confusion
- **Cons**: Minor caller update
- **Effort**: Small
- **Risk**: Low

### Rename parameter to durationMs, keep conversion internal (already correct)
- **Pros**: No caller change
- **Cons**: Still leaky
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change signature to RecordOpenDuration(string groupName, TimeSpan duration) and use duration.TotalSeconds internally. Update call sites.

## Acceptance Criteria

- [ ] RecordOpenDuration accepts TimeSpan or consistently named durationMs parameter
- [ ] Internal conversion correct

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
