---
status: done
priority: p2
issue_id: "012"
tags: []
dependencies: []
---

# Add row locks to retry processor queries

## Problem Statement

PostgreSqlDataStorage.cs:456-506 and SqlServerDataStorage.cs:463-465 select retry messages without FOR UPDATE SKIP LOCKED, allowing duplicates.

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
- [x] Add FOR UPDATE SKIP LOCKED (PostgreSQL) or UPDLOCK hint (SQL Server) to retry queries

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

### 2026-01-25 - Completed

**By:** Agent
**Actions:**
- Added `FOR UPDATE SKIP LOCKED` to PostgreSQL `_GetMessagesOfNeedRetryAsync` query
- Added `UPDLOCK` hint to SQL Server `_GetMessagesOfNeedRetryAsync` query (changed from `WITH (READPAST)` to `WITH (UPDLOCK, READPAST)`)
- Status changed: ready → done

### 2026-01-29 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
