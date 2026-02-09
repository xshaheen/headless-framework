---
status: ready
priority: p1
issue_id: "021"
tags: ["di-lifetime","code-review","scheduling"]
dependencies: []
---

# Verify IScheduledJobStorage lifetime avoids captive dependency

## Problem Statement

ScheduledJobManager, SchedulerBackgroundService, and other singletons capture IScheduledJobStorage via constructor injection. If IScheduledJobStorage is registered as Scoped (common for DB-backed services), this is a captive dependency — a singleton holding a scoped service, leading to stale state or ObjectDisposedException.

## Findings

- **Location:** src/Headless.Messaging.Core/Setup.cs:212, 222
- **Risk:** Critical - captive dependency if storage is scoped
- **Reviewer:** strict-dotnet-reviewer

## Proposed Solutions

### Enforce singleton registration of IScheduledJobStorage
- **Pros**: Simple; document the contract
- **Cons**: Limits provider flexibility
- **Effort**: Small
- **Risk**: Low

### Resolve IScheduledJobStorage from scope in background services
- **Pros**: Safe regardless of provider lifetime
- **Cons**: More boilerplate; scope-per-operation
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Document that IScheduledJobStorage must be singleton-safe (each method opens its own connection). Add runtime validation via IServiceProviderIsService or startup check.

## Acceptance Criteria

- [ ] Lifetime contract documented on IScheduledJobStorage interface
- [ ] Runtime validation prevents scoped registration when consumed by singletons

## Notes

PR #170 code review finding. PostgreSqlScheduledJobStorage is already singleton-safe (connection-per-call), but the contract is implicit.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-08 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
