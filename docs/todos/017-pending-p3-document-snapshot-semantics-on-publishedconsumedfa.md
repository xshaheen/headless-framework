---
status: pending
priority: p3
issue_id: "017"
tags: ["code-review","documentation","performance"]
dependencies: []
---

# Document snapshot semantics on Published/Consumed/Faulted properties

## Problem Statement

Published/Consumed/Faulted properties call .ToArray() on every access, allocating a new array each time. Multiple assertion chains on the same property create independent snapshots that could differ if messages arrive between calls.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessageObservationStore.cs:20-26
- **Discovered by:** performance-oracle

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add XML doc warning that each access returns a new snapshot. Consider adding explicit Snapshot() methods in future.

## Acceptance Criteria

- [ ] XML docs mention snapshot behavior on these properties

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
