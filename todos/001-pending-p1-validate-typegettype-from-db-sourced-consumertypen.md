---
status: pending
priority: p1
issue_id: "001"
tags: ["security","code-review","scheduling"]
dependencies: []
---

# Validate Type.GetType from DB-sourced ConsumerTypeName

## Problem Statement

ScheduledJobDispatcher.cs:44-53 calls Type.GetType(job.ConsumerTypeName) using a value sourced from the database without any type validation or allowlisting. If the DB is compromised, this enables arbitrary type instantiation (potential RCE).

## Findings

- **Location:** src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs:44-53
- **Risk:** High - arbitrary type instantiation from untrusted DB data
- **Reviewer:** security-sentinel, strict-dotnet-reviewer

## Proposed Solutions

### Type allowlist via IScheduledJobDefinition registry
- **Pros**: Only registered types can be instantiated; zero runtime overhead
- **Cons**: Requires registry lookup
- **Effort**: Small
- **Risk**: Low

### Validate type implements IConsume<ScheduledTrigger> before instantiation
- **Pros**: Simple interface check
- **Cons**: Still allows any type implementing the interface
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Check resolved type against registered job definitions or verify it implements IConsume<ScheduledTrigger> before ActivatorUtilities.CreateInstance

## Acceptance Criteria

- [ ] Type.GetType result validated before instantiation
- [ ] Test covers rejected type scenario

## Notes

PR #170 code review finding. Keyed DI path is safe; only the ActivatorUtilities fallback path is affected.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
