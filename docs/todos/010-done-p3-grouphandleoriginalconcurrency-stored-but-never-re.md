---
status: done
priority: p3
issue_id: "010"
tags: ["code-review","messaging","dotnet","quality"]
dependencies: []
---

# GroupHandle.OriginalConcurrency stored but never read

## Problem Statement

GroupHandle.OriginalConcurrency is assigned from _options.ConsumerThreadCount but never referenced after construction. Classic YAGNI — unused stored state.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:GroupHandle.OriginalConcurrency
- **Discovered by:** code-simplicity-reviewer, architecture-strategist

## Proposed Solutions

### Remove OriginalConcurrency from GroupHandle
- **Pros**: Cleaner, no dead state
- **Cons**: Will be needed if P1 todo for _ResumeGroupAsync restart is fixed
- **Effort**: Tiny
- **Risk**: Low


## Recommended Action

Keep OriginalConcurrency if implementing the _ResumeGroupAsync task restart (P1 todo), since it will be needed. Otherwise remove.

## Acceptance Criteria

- [ ] No unused properties on GroupHandle

## Notes

PR #194 review. Depends on P1 _ResumeGroupAsync restart decision.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-20 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
