---
status: pending
priority: p1
issue_id: "022"
tags: ["correctness","code-review","scheduling"]
dependencies: []
---

# Use await using for distributed lock disposal

## Problem Statement

SchedulerBackgroundService.cs uses try/finally with explicit ReleaseAsync() for distributed lock, but doesn't use await using. If ReleaseAsync itself throws, the lock is leaked. Also, no CancellationToken on release means unbounded I/O during shutdown.

## Findings

- **Location:** src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs:102-129
- **Risk:** High - lock leak if ReleaseAsync throws; unbounded shutdown I/O
- **Reviewer:** strict-dotnet-reviewer

## Proposed Solutions

### Use await using if IDistributedLock implements IAsyncDisposable
- **Pros**: Guarantees release even if ReleaseAsync throws
- **Cons**: Requires IAsyncDisposable on lock
- **Effort**: Small
- **Risk**: Low

### Add bounded CancellationTokenSource for release call
- **Pros**: Prevents unbounded shutdown I/O
- **Cons**: Slightly more code
- **Effort**: Small
- **Risk**: Low


## Recommended Action

If IDistributedLock is IAsyncDisposable, use await using. Add a timeout-bounded CTS for the release call during shutdown.

## Acceptance Criteria

- [ ] Lock released via await using or equivalent guaranteed disposal
- [ ] Release call has bounded timeout

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
