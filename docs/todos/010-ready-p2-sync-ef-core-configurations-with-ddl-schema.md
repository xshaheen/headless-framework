---
status: ready
priority: p2
issue_id: "010"
tags: ["data-integrity","code-review","scheduling","ef-core"]
dependencies: []
---

# Sync EF Core configurations with DDL schema

## Problem Statement

EF Core IEntityTypeConfiguration for ScheduledJob and JobExecution are missing 5+ property mappings that exist in the raw DDL. This creates schema drift risk when using EF migrations alongside raw SQL initialization.

## Findings

- **Location:** src/Headless.Messaging.PostgreSql/EntityConfigurations/
- **Risk:** Medium - EF migrations may generate incorrect schema changes
- **Reviewer:** data-integrity-guardian, strict-dotnet-reviewer

## Proposed Solutions

### Add missing property mappings to EF configs
- **Pros**: Schema parity; safe migrations
- **Cons**: Manual sync needed
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Audit all ScheduledJob and JobExecution properties against DDL and add missing HasColumnName/HasColumnType/IsRequired mappings.

## Acceptance Criteria

- [ ] All DDL columns have matching EF configuration
- [ ] EF model snapshot matches DDL schema

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
