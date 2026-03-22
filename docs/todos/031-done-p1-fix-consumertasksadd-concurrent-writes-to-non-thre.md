---
status: done
priority: p1
issue_id: "031"
tags: ["code-review","threading","correctness"]
dependencies: []
---

# Fix ConsumerTasks.Add concurrent writes to non-thread-safe List<Task>

## Problem Statement

GroupHandle.ConsumerTasks (IConsumerRegister.cs:645) is a plain List<Task>. ConsumerTasks.Add(task) is called at line 267 in a loop that spawns LongRunning tasks. Two consumer threads for the same group starting simultaneously can corrupt the list. PulseAsync then iterates via SelectMany — on a half-written list this can throw or skip tasks.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:645,267
- **Risk:** High — list corruption from concurrent Add, missed tasks in PulseAsync
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Use ConcurrentBag<Task>
- **Pros**: Thread-safe, lock-free
- **Cons**: No ordering guarantees (not needed here)
- **Effort**: Small
- **Risk**: Low

### Lock on _clientsLock when adding
- **Pros**: Consistent with existing lock pattern in GroupHandle
- **Cons**: Slightly more code
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Use ConcurrentBag<Task> — simpler and no lock needed. The collection is only iterated in PulseAsync which already snapshots.

## Acceptance Criteria

- [ ] ConsumerTasks uses a thread-safe collection
- [ ] PulseAsync iteration is safe against concurrent mutation
- [ ] No List<Task> remaining in GroupHandle

## Notes

Low cardinality (1-4 threads per group), so either approach works.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-22 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
