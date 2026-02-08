---
status: pending
priority: p3
issue_id: "016"
tags: ["maintainability","code-review","scheduling","postgresql"]
dependencies: []
---

# Use named column access in DbDataReader instead of ordinal indices

## Problem Statement

PostgreSqlScheduledJobStorage.cs uses reader.GetGuid(0), reader.GetString(1), etc. Any column reordering in SELECT breaks all subsequent reads silently (wrong data, not exceptions).

## Findings

- **Location:** src/Headless.Messaging.PostgreSql/PostgreSqlScheduledJobStorage.cs
- **Reviewer:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### Use reader.GetOrdinal(name) + reader.GetX(ordinal)
- **Pros**: Resilient to column reorder; self-documenting
- **Cons**: Slightly more verbose
- **Effort**: Small
- **Risk**: Low

### Use Dapper or similar micro-ORM
- **Pros**: Eliminates manual mapping entirely
- **Cons**: New dependency
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Cache ordinals via reader.GetOrdinal("column_name") at start of each mapping method. Keep raw SQL approach.

## Acceptance Criteria

- [ ] No hardcoded ordinal indices in reader access

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
