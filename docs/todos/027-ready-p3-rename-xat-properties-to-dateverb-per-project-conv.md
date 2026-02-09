---
status: ready
priority: p3
issue_id: "027"
tags: ["naming","code-review","scheduling"]
dependencies: []
---

# Rename XAt properties to Date{Verb} per project convention

## Problem Statement

ScheduledJob.LockedAt, JobExecution.StartedAt, JobExecution.CompletedAt use the XAt suffix. Project convention (CLAUDE.md) states: 'Avoid naming date fields/properties with the XAt suffix. Use DateCreated, DateUpdated, DateDeleted.' These should be DateLocked, DateStarted, DateCompleted.

## Findings

- **Location:** ScheduledJob.cs:122, JobExecution.cs:35,43
- **Reviewer:** pattern-recognition-specialist

## Proposed Solutions

### Rename to DateLocked, DateStarted, DateCompleted
- **Pros**: Consistent with project convention
- **Cons**: Breaking change for consumers; requires DB column rename or mapping
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Rename properties and update SQL column names or add EF column mappings.

## Acceptance Criteria

- [ ] No XAt-suffixed date properties in scheduling entities

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-09 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
