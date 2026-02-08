---
status: pending
priority: p2
issue_id: "012"
tags: ["api","code-review","scheduling"]
dependencies: []
---

# Handle ScheduledJobManager exceptions in dashboard endpoints

## Problem Statement

RouteActionProvider.cs dashboard endpoints call ScheduledJobManager methods that throw InvalidOperationException (e.g., job not found). These propagate as HTTP 500 instead of appropriate 404/409 responses.

## Findings

- **Location:** src/Headless.Messaging.Dashboard/RouteActionProvider.cs
- **Risk:** Medium - poor API error responses; misleading status codes
- **Reviewer:** security-sentinel, pragmatic-dotnet-reviewer

## Proposed Solutions

### Catch specific exceptions and map to HTTP status codes
- **Pros**: Correct REST semantics
- **Cons**: Try-catch per endpoint
- **Effort**: Small
- **Risk**: Low

### Return DataResult from manager methods instead of throwing
- **Pros**: Follows framework pattern; no exceptions for expected failures
- **Cons**: API change to ScheduledJobManager
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Adopt DataResult pattern (framework convention) in ScheduledJobManager. Dashboard maps result to appropriate HTTP status.

## Acceptance Criteria

- [ ] Job-not-found returns 404
- [ ] Invalid state transitions return 409
- [ ] No 500s for expected failures

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
