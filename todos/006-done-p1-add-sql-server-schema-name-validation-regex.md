---
status: done
priority: p1
issue_id: "006"
tags: []
dependencies: []
---

# Add SQL Server schema name validation regex

## Problem Statement

SqlServerEntityMessagingOptions.cs only validates length, not characters. Allows SQL injection via schema name.

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
- [x] Add regex validation matching PostgreSQL pattern: ^[a-zA-Z_][a-zA-Z0-9_]{0,127}$

## Notes

Source: Workflow automation

## Work Log

### 2026-01-24 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-24 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-29 - Completed

**By:** Agent
**Actions:**
- Fixed class name mismatch: `SqlServerEntityFrameworkMessagingOptions` → `SqlServerEntityMessagingOptions`
- Regex validation was already implemented with `_ValidIdentifier()` pattern
- Pattern: `^[a-zA-Z_@#][a-zA-Z0-9_@#$]{0,127}$` (SQL Server identifier rules)
- Status changed: ready → done

### 2026-01-29 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
