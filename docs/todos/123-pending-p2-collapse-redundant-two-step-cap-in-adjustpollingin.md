---
status: pending
priority: p2
issue_id: "123"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Collapse redundant two-step cap in _AdjustPollingInterval to single expression

## Problem Statement

In MessageNeedToRetryProcessor._AdjustPollingInterval, the backoff doubling logic uses two variables where one suffices. `doubled` is always <= _maxInterval.Ticks, making `newTicks = doubled < _maxInterval.Ticks ? doubled : _maxInterval.Ticks` dead code (condition always false or equal). The dead second line makes readers question whether the overflow fix is complete.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:283-285
- **Risk:** Dead branch misleads future maintainers
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer, compound-engineering:review:code-simplicity-reviewer

## Proposed Solutions

### Collapse to: `var newTicks = current <= _maxInterval.Ticks / 2 ? current * 2 : _maxInterval.Ticks;`
- **Pros**: Removes dead code, intent clearer
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace both lines with single ternary expression.

## Acceptance Criteria

- [ ] Single expression for newTicks
- [ ] No redundant second cap variable
- [ ] Adaptive polling tests pass

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
