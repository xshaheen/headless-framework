---
status: pending
priority: p3
issue_id: "046"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Simplify two-counter adaptive polling logic in _AdjustPollingInterval

## Problem Statement

MessageNeedToRetryProcessor._AdjustPollingInterval maintains two counters: _consecutiveHealthyCycles and _consecutiveCleanCycles. In the total==0 branch, both are incremented simultaneously, so they are always equal in that path. _consecutiveCleanCycles >= 3 (resets to base) will always fire before _consecutiveHealthyCycles >= 2 does anything useful. The two-counter interaction in the zero-message branch has a subtle non-obvious precedence. The intent — reset to base after sustained zero-message cycles, halve after sustained healthy-but-some-message cycles — can be expressed with one counter and clear branching.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:228-291 - _AdjustPollingInterval
- **Discovered by:** compound-engineering:review:code-simplicity-reviewer

## Proposed Solutions

### Collapse to single counter with clearly separated branches
- **Pros**: Clearer intent, fewer state variables
- **Cons**: Behavioral change must be validated
- **Effort**: Medium
- **Risk**: Low

### Add comments explaining the counter interaction
- **Pros**: No behavioral change
- **Cons**: Still two counters
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add detailed comments documenting why both counters increment in the zero-message branch and the precedence relationship between the two threshold checks. Refactoring to a single counter is preferred if tests are updated.

## Acceptance Criteria

- [ ] Counter interaction in zero-message branch is clearly documented or simplified
- [ ] Existing adaptive polling tests still pass

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
