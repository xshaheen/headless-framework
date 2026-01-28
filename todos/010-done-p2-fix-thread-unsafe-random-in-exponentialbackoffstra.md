---
status: done
priority: p2
issue_id: "010"
tags: []
dependencies: []
---

# Fix thread-unsafe Random in ExponentialBackoffStrategy

## Problem Statement

ExponentialBackoffStrategy.cs:15 uses instance Random which is not thread-safe. Concurrent calls corrupt state.

## Findings

- **Status:** Identified during workflow execution
- **Priority:** p2

## Proposed Solutions

### Option 1: [Primary solution]
- **Pros**: [Benefits]
- **Cons**: [Drawbacks]
- **Effort**: Small/Medium/Large
- **Risk**: Low/Medium/High

## Recommended Action

[To be filled during triage]

## Acceptance Criteria
- [ ] Use Random.Shared (thread-safe in .NET 6+) or ThreadLocal<Random>

## Notes

Source: Workflow automation

## Work Log

### 2026-01-24 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-25 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-27 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
