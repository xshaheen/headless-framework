---
status: pending
priority: p2
issue_id: "003"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Preserve manual disable state when reconciling scheduled jobs

## Problem Statement

ScheduledJobReconciler always builds jobs with IsEnabled=true and UpsertJobAsync updates IsEnabled/NextRunTime on conflict. This re-enables jobs that an operator has disabled on every restart, defeating manual controls and creating surprise executions.

## Findings

- **Reconciler always sets enabled:** /Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.Core/Scheduling/ScheduledJobReconciler.cs:79-99
- **Upsert overwrites enabled state:** /Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.PostgreSql/PostgreSqlScheduledJobStorage.cs:168-177

## Proposed Solutions

### Preserve IsEnabled/Status/NextRunTime on upsert
- **Pros**: Keeps operator intent across restarts, minimal API changes
- **Cons**: Requires changes to upsert SQL and in-memory behavior
- **Effort**: Small
- **Risk**: Low

### Reconciler reads existing job first
- **Pros**: Explicit control in reconciler, works across providers
- **Cons**: Extra read per job on startup
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Update upsert semantics (and in-memory equivalent) to only change definition fields when a job already exists, preserving IsEnabled/Status/NextRunTime unless the job is new.

## Acceptance Criteria

- [ ] Disabled jobs remain disabled after restart
- [ ] Reconciler only updates cron/definition fields for existing jobs
- [ ] Regression test covers manual disable persistence

## Notes

Found during PR #176 review

## Work Log

### 2026-02-10 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
