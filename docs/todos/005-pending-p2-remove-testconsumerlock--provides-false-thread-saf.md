---
status: pending
priority: p2
issue_id: "005"
tags: ["code-review","quality"]
dependencies: []
---

# Remove TestConsumer._lock — provides false thread safety

## Problem Statement

TestConsumer<T>._lock is held only in Clear() but Consume() enqueues without the lock. The lock implies atomicity it doesn't provide — a concurrent Enqueue can interleave between TryDequeue calls inside Clear(). ConcurrentQueue is already thread-safe; the lock is noise.

## Findings

- **Location:** src/Headless.Messaging.Testing/TestConsumer.cs:16-31
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Remove _lock field and the lock statement in Clear(). Document Clear() as best-effort drain intended for use between tests.

## Acceptance Criteria

- [ ] _lock field removed from TestConsumer
- [ ] Clear() drains without lock
- [ ] XML doc on Clear() notes it should be called between tests when harness is not actively consuming

## Notes

Source: Code review

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
