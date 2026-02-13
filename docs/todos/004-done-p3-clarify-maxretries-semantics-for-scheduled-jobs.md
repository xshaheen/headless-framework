---
status: done
priority: p3
issue_id: "004"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Clarify MaxRetries semantics for scheduled jobs

## Problem Statement

ScheduledJob.MaxRetries is documented as the maximum retry count, but scheduler logic uses it as a mutable retry attempt counter (incremented on failure and reset on success). This mismatch is confusing for API consumers and implies unlimited retries contrary to the property name and XML docs.

## Findings

- **Property docs:** /Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Abstractions/Scheduling/ScheduledJob.cs:89-93
- **Runtime usage:** /Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs:168-299

## Proposed Solutions

### Rename to RetryCount and introduce MaxRetries
- **Pros**: Clear API intent, supports capped retries
- **Cons**: Breaking change in public API
- **Effort**: Medium
- **Risk**: Medium

### Keep name but update docs and enforce max
- **Pros**: Minimal API change
- **Cons**: Still ambiguous naming
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Pick one: either rename to RetryCount and add a real MaxRetries limit, or update docs and enforce a capped retry policy derived from RetryIntervals to align behavior with the API.

## Acceptance Criteria

- [x] Retry semantics are explicitly documented
- [x] Max retry limit is enforced or clearly stated
- [x] Scheduler behavior and public docs are consistent

## Notes

Found during PR #176 review

## Work Log

### 2026-02-10 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-10 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-02-10 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
