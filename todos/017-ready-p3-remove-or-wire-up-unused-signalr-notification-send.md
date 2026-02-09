---
status: ready
priority: p3
issue_id: "017"
tags: ["dead-code","code-review","scheduling"]
dependencies: []
---

# Remove or wire up unused SignalR notification sender

## Problem Statement

SchedulingNotificationSender is registered in DI but never injected or called anywhere. This is dead code that suggests incomplete integration.

## Findings

- **Location:** src/Headless.Messaging.Core/ (SchedulingNotificationSender registration)
- **Reviewer:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### Wire up to SchedulerBackgroundService on state changes
- **Pros**: Completes the feature
- **Cons**: More work now
- **Effort**: Medium
- **Risk**: Low

### Remove until actually needed
- **Pros**: Clean codebase; YAGNI
- **Cons**: Re-implementation later
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove the unused registration and class. Re-add when dashboard live updates are actually implemented.

## Acceptance Criteria

- [ ] No dead SignalR code registered in DI

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-09 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
