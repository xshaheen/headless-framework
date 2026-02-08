---
status: pending
priority: p1
issue_id: "006"
tags: ["thread-safety","code-review","scheduling"]
dependencies: []
---

# Fix InMemory storage thread safety gap in Update/Delete

## Problem Statement

InMemoryScheduledJobStorage.cs uses SemaphoreSlim for AcquireDueJobsAsync and AcquireForExecutionAsync but UpdateJobAsync and DeleteJobAsync bypass the semaphore entirely. Under concurrent access, ConcurrentDictionary alone doesn't guarantee atomic read-modify-write sequences.

## Findings

- **Location:** src/Headless.Messaging.InMemoryStorage/InMemoryScheduledJobStorage.cs
- **Risk:** High for test correctness - inconsistent state during concurrent test execution
- **Reviewer:** performance-oracle

## Proposed Solutions

### Acquire _lock in UpdateJobAsync and DeleteJobAsync
- **Pros**: Consistent with other methods; prevents races
- **Cons**: Slight serialization in tests
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Wrap UpdateJobAsync and DeleteJobAsync bodies in await _lock.WaitAsync(ct) with try/finally release, matching existing pattern.

## Acceptance Criteria

- [ ] All mutation methods acquire _lock semaphore
- [ ] Concurrent test scenario passes

## Notes

PR #170 code review finding. InMemory storage is used for tests and dev, so correctness matters.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
