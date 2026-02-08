---
status: pending
priority: p3
issue_id: "019"
tags: ["dx","code-review","scheduling"]
dependencies: []
---

# Add startup validation warning when job definitions exist but no storage

## Problem Statement

When IScheduledJobDefinition services are registered but no IScheduledJobStorage provider is configured, the scheduler silently does nothing. Users may not realize scheduling is non-functional.

## Findings

- **Location:** src/Headless.Messaging.Core/Setup.cs (_RegisterSchedulerServices)
- **Reviewer:** architecture-strategist

## Proposed Solutions

### Log warning on startup when definitions exist but no storage
- **Pros**: Clear feedback; easy to spot
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

In hosted service StartAsync, check for definitions without storage and log a Warning.

## Acceptance Criteria

- [ ] Warning logged when definitions registered without storage provider

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
