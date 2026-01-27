---
status: ready
priority: p1
issue_id: "004"
tags: []
dependencies: []
---

# Add PostgreSQL message deduplication with ON CONFLICT

## Problem Statement

PostgreSqlDataStorage.cs uses plain INSERT that throws on duplicate instead of UPSERT like SQL Server MERGE. Causes infinite retry on redelivery.

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
- [ ] Use ON CONFLICT DO UPDATE for StoreReceivedMessageAsync like SQL Server's MERGE pattern

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
