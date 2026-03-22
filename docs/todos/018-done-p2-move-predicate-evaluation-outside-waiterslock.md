---
status: done
priority: p2
issue_id: "018"
tags: ["code-review","concurrency","security"]
dependencies: []
---

# Move predicate evaluation outside _waitersLock

## Problem Statement

User-supplied predicates execute inside _waitersLock in both Record() and _FindExisting(). If a predicate re-enters the harness (e.g. harness.Published.Any()), it deadlocks.

## Findings

- **Record() location:** src/Headless.Messaging.Testing/MessageObservationStore.cs:43
- **_FindExisting() location:** src/Headless.Messaging.Testing/MessageObservationStore.cs:160
- **Discovered by:** security-sentinel

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Snapshot candidates under lock, evaluate predicates outside lock. TrySetResult is idempotent.

## Acceptance Criteria

- [ ] Predicates execute outside _waitersLock
- [ ] No deadlock when predicate calls harness.Published
- [ ] Existing tests still pass

## Notes

Source: Code review

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
