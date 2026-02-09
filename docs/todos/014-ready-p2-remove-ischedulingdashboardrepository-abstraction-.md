---
<<<<<<<< HEAD:docs/todos/014-ready-p2-remove-ischedulingdashboardrepository-abstraction-.md
status: ready
|||||||| 6e93708f:todos/014-pending-p2-remove-ischedulingdashboardrepository-abstraction-.md
status: pending
========
status: done
>>>>>>>> refs/heads/codex/cache-perf-fixes:docs/todos/014-done-p2-remove-ischedulingdashboardrepository-abstraction-.md
priority: p2
issue_id: "014"
tags: ["architecture","performance","code-review","scheduling"]
dependencies: []
---

# Remove ISchedulingDashboardRepository abstraction or reduce 10K fetch

## Problem Statement

ISchedulingDashboardRepository fetches up to 10,000 execution rows for in-memory aggregation in the scheduling graph endpoint. The abstraction adds a layer without clear benefit since it's only used by the dashboard.

## Findings

- **Location:** src/Headless.Messaging.Dashboard/RouteActionProvider.cs (graph endpoint)
- **Risk:** Medium - excessive memory usage for graph; unnecessary abstraction
- **Reviewer:** pragmatic-dotnet-reviewer, performance-oracle

## Proposed Solutions

### Push aggregation to SQL query (GROUP BY time buckets)
- **Pros**: Massive perf improvement; DB does the work
- **Cons**: SQL complexity
- **Effort**: Medium
- **Risk**: Low

### Reduce limit and add pagination
- **Pros**: Quick fix; bounded memory
- **Cons**: Graph may be incomplete
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Push time-bucket aggregation into SQL. Remove ISchedulingDashboardRepository if it's the only consumer — inline the query.

## Acceptance Criteria

- [x] Graph endpoint does not fetch 10K rows into memory
- [x] SQL performs aggregation server-side

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
<<<<<<<< HEAD:docs/todos/014-ready-p2-remove-ischedulingdashboardrepository-abstraction-.md
|||||||| 6e93708f:todos/014-pending-p2-remove-ischedulingdashboardrepository-abstraction-.md
========

### 2026-02-09 - Implemented

**By:** Agent
**Actions:**
- Removed `ISchedulingDashboardRepository` interface and `SchedulingDashboardRepository` implementation
- Added `GetExecutionStatusCountsAsync` to `IScheduledJobStorage` with SQL GROUP BY aggregation
- PostgreSQL: server-side DATE_TRUNC + GROUP BY replaces 10K row fetch
- InMemory: equivalent LINQ GroupBy for dev/test
- Dashboard endpoints now use `IScheduledJobStorage` directly
- Status changed: ready → done
>>>>>>>> refs/heads/codex/cache-perf-fixes:docs/todos/014-done-p2-remove-ischedulingdashboardrepository-abstraction-.md
