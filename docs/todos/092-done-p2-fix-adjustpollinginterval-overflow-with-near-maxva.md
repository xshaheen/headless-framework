---
status: done
priority: p2
issue_id: "092"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix _AdjustPollingInterval overflow with near-MaxValue intervals

## Problem Statement

In IProcessor.NeedRetry._AdjustPollingInterval, current * 2 is a long multiplication. If current > long.MaxValue/2 (possible if MaxPollingInterval is set very high), this overflows to a negative value. The doubled < _maxInterval.Ticks check then passes (negative < positive), setting _currentIntervalTicks to a negative value causing undefined polling behavior.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs (~line 4157-4173)
- **Risk:** Negative polling interval if MaxPollingInterval set near TimeSpan.MaxValue
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Use Math.Min(current, _maxInterval.Ticks / 2) * 2 to bound before multiplying
- **Pros**: Prevents overflow, simple
- **Cons**: None
- **Effort**: Small
- **Risk**: Low

### Add validator max bound for MaxPollingInterval
- **Pros**: Catches misconfiguration at startup
- **Cons**: Does not fix the overflow itself
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Both: add a reasonable max bound (e.g., 24h) to RetryProcessorOptionsValidator for MaxPollingInterval, and guard the multiplication with Math.Min(current, _maxInterval.Ticks / 2) * 2.

## Acceptance Criteria

- [ ] No integer overflow possible in _AdjustPollingInterval for any valid MaxPollingInterval
- [ ] Validator rejects unreasonably large MaxPollingInterval values
- [ ] Test added for near-max interval doubling

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
