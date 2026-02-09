---
status: pending
priority: p1
issue_id: "001"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Define ScheduledJobConcurrencyException for scheduler storage

## Problem Statement

IScheduledJobStorage.UpdateJobAsync advertises ScheduledJobConcurrencyException, and InMemoryScheduledJobStorage throws it, but the type is not defined anywhere in the solution. This is a compile-time break for Headless.Messaging.InMemoryStorage and prevents consumers from handling concurrency errors as documented.

## Findings

- **Location:** /Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.InMemoryStorage/InMemoryScheduledJobStorage.cs:112-115
- **Contract:** /Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Abstractions/Scheduling/IScheduledJobStorage.cs:83-91
- **Impact:** Build fails due to missing type; consumers cannot catch the documented exception.

## Proposed Solutions

### Add ScheduledJobConcurrencyException to Abstractions
- **Pros**: Fixes build, aligns with interface contract, reusable across storage providers
- **Cons**: Requires adding new public type
- **Effort**: Small
- **Risk**: Low

### Replace with existing exception type
- **Pros**: No new public type
- **Cons**: Breaks interface documentation and existing code expectations
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Add ScheduledJobConcurrencyException to the Abstractions package and keep UpdateJobAsync throwing it.

## Acceptance Criteria

- [ ] ScheduledJobConcurrencyException exists in Headless.Messaging.Abstractions
- [ ] Headless.Messaging.InMemoryStorage compiles without missing type errors
- [ ] UpdateJobAsync continues to throw the new exception on version mismatch

## Notes

Found during PR #176 review

## Work Log

### 2026-02-10 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
