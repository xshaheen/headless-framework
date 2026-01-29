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
- [ ] Add regex validation matching PostgreSQL pattern: ^[a-zA-Z_][a-zA-Z0-9_]{0,127}$

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
