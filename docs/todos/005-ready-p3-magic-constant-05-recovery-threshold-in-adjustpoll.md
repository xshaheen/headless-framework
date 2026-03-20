---
status: ready
priority: p3
issue_id: "005"
tags: ["code-review","messaging","retry","dotnet"]
dependencies: []
---

# Magic constant 0.5 recovery threshold in _AdjustPollingInterval inconsistent with configurable backoff threshold

## Problem Statement

_AdjustPollingInterval uses a hardcoded 0.5 as the recovery threshold (below which intervals halve). The backoff threshold is configurable via TransientFailureRateThreshold. If a user sets TransientFailureRateThreshold = 0.3, the recovery threshold (0.5) is above the backoff threshold, causing oscillation: back off when rate > 0.3, recover when rate <= 0.5 — which includes all rates between 0.3 and 0.5, so intervals may oscillate on every cycle.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:_AdjustPollingInterval
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Derive recovery threshold as threshold/2 or make it a separate RetryProcessorOptions property
- **Pros**: Consistent behavior across all threshold configurations
- **Cons**: Minor change or API addition
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add RetryProcessorOptions.RecoveryRateThreshold defaulting to 0.5 or derive it as TransientFailureRateThreshold / 2 to ensure recovery threshold is always below backoff threshold.

## Acceptance Criteria

- [ ] Recovery threshold < backoff threshold in all configurations
- [ ] No oscillation when TransientFailureRateThreshold < 0.5

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
