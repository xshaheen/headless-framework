---
status: ready
priority: p3
issue_id: "082"
tags: ["code-review","testing","simplicity","messaging"]
dependencies: []
---

# Refactor adaptive polling tests to avoid reflection — extract AdaptivePollingState struct

## Problem Statement

MessageNeedToRetryProcessorTests uses BindingFlags.NonPublic|Instance reflection for 4 private members (_GetCurrentInterval, _SetCurrentInterval, _InvokeAdjustPollingInterval, _GetLockTtl). This couples tests to implementation details — any refactoring breaks tests silently. CurrentPollingInterval is already public via IRetryProcessorMonitor, which the tests could use instead.

## Findings

- **Location:** tests/Headless.Messaging.Core.Tests.Unit/Processor/MessageNeedToRetryProcessorTests.cs:102-136
- **Problem:** 4 reflection helpers coupling tests to private implementation details
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

### Use IRetryProcessorMonitor.CurrentPollingInterval for read tests
- **Pros**: No reflection for reads, uses public interface
- **Cons**: Write/invoke helpers still needed unless logic extracted
- **Effort**: Small
- **Risk**: Low

### Extract AdaptivePollingState internal struct with InternalsVisibleTo
- **Pros**: All adaptive polling logic directly testable without reflection
- **Cons**: More code, separate struct
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Start by replacing _GetCurrentInterval usage with IRetryProcessorMonitor.CurrentPollingInterval. Then evaluate extracting AdaptivePollingState if reflection for invoke helpers remains.

## Acceptance Criteria

- [ ] No BindingFlags.NonPublic reflection in adaptive polling tests
- [ ] Tests still cover all interval adjustment scenarios
- [ ] IRetryProcessorMonitor.CurrentPollingInterval used for state reads

## Notes

PR #194 code review finding.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
