---
status: ready
priority: p1
issue_id: "033"
tags: []
dependencies: []
---

# async-correctness-missing-anycontext

## Problem Statement

Library code missing ConfigureAwait(false) or AnyContext() extensions. Can capture SynchronizationContext causing deadlocks in UI apps or sync-over-async scenarios.

## Findings

- **Status:** Identified during workflow execution
- **Priority:** p1

## Proposed Solutions

### Option 1: [Primary solution]
- **Pros**: [Benefits]
- **Cons**: [Drawbacks]
- **Effort**: Small/Medium/Large
- **Risk**: Low/Medium/High

## Recommended Action

[To be filled during triage]

## Acceptance Criteria
- [ ] Add .AnyContext() to all async calls
- [ ] Audit all await statements in library code
- [ ] Add analyzer rule enforcing AnyContext
- [ ] All library awaits use AnyContext()
- [ ] No SynchronizationContext captured

## Notes

Source: Workflow automation

## Work Log

### 2026-01-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
