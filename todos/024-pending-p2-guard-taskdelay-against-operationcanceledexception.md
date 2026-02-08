---
status: pending
priority: p2
issue_id: "024"
tags: ["correctness","code-review","scheduling"]
dependencies: []
---

# Guard Task.Delay against OperationCanceledException on shutdown

## Problem Statement

SchedulerBackgroundService and StaleJobRecoveryService have Task.Delay outside the try-catch. When stoppingToken fires during delay, OperationCanceledException propagates unhandled to BackgroundService base class. While functionally benign, it's inconsistent with the explicit cancellation handling inside the loop.

## Findings

- **Location:** src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs:63, StaleJobRecoveryService.cs:61
- **Risk:** Low-Medium - benign but inconsistent shutdown path
- **Reviewer:** strict-dotnet-reviewer

## Proposed Solutions

### Wrap Task.Delay in try-catch with break on cancellation
- **Pros**: Clean shutdown; consistent pattern
- **Cons**: Slightly more code
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Wrap Task.Delay in try/catch(OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }

## Acceptance Criteria

- [ ] Task.Delay cancellation handled explicitly in both services

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
