---
status: ready
priority: p2
issue_id: "013"
tags: []
dependencies: []
---

# Parameterize ID values in SQL DELETE queries

## Problem Statement

PostgreSqlDataStorage.cs:332 and SqlServerDataStorage.cs:317,328 interpolate IDs directly into SQL strings instead of parameters.

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
- [ ] Use @Id parameter instead of string interpolation for consistency and safety

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
