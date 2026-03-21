---
status: done
priority: p2
issue_id: "017"
tags: ["code-review","dotnet","quality","performance"]
dependencies: []
---

# Make group client pause and resume thread-safe

## Problem Statement

`GroupHandle.Clients` is mutated under a lock in `AddClient`, but pause, resume, and disposal iterate the same mutable list without taking that lock or snapshotting it first. If the circuit trips while consumers are still starting, enumeration can race with `AddClient`, causing `Collection was modified` exceptions or leaving newly-added clients unpaused.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:198
- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:253
- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:275
- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:486
- **Risk:** Medium - startup-time circuit trips can throw or miss pausing some clients
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### Snapshot clients under the same lock before iterating
- **Pros**: Localized fix with low surface area
- **Cons**: Still needs explicit paused-state handling for later clients
- **Effort**: Small
- **Risk**: Low

### Replace the mutable list with a collection or handle abstraction that preserves paused state
- **Pros**: Stronger lifecycle guarantees
- **Cons**: More refactoring
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Take a snapshot under lock before pause, resume, or dispose iteration, and ensure clients added after a pause inherit the paused state.

## Acceptance Criteria

- [ ] Pause, resume, and dispose can run while clients are still being added without throwing
- [ ] Clients added after a group is paused do not continue consuming unexpectedly
- [ ] Tests cover startup-time pause and resume races

## Notes

Discovered during PR #194 code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
