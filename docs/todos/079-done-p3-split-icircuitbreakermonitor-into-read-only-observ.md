---
status: done
priority: p3
issue_id: "079"
tags: ["code-review","simplicity","security","api-design","messaging"]
dependencies: []
---

# Split ICircuitBreakerMonitor into read-only observer and operator interfaces

## Problem Statement

ICircuitBreakerMonitor exposes ForceOpenAsync and ResetAsync alongside read-only methods. Any code that injects ICircuitBreakerMonitor for health checks can also forcibly pause consumers. This is a privilege escalation surface. Additionally KnownGroups has no current callers in production code (YAGNI). The correct pattern is a read-only ICircuitBreakerObserver injected by default, with ICircuitBreakerOperator available only to management endpoints.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs:121-133
- **Problem:** Write methods on read-mostly interface create privilege escalation surface
- **YAGNI:** KnownGroups has no production callers in this PR
- **Discovered by:** code-simplicity-reviewer, pragmatic-dotnet-reviewer, security-sentinel

## Proposed Solutions

### Split into ICircuitBreakerObserver (read) and ICircuitBreakerOperator (write)
- **Pros**: Type-safe privilege separation, clear DI contract
- **Cons**: More interfaces, more DI registrations
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Split the interface. Register ICircuitBreakerObserver for general injection. Keep ICircuitBreakerOperator separate for admin/management endpoints.

## Acceptance Criteria

- [ ] Read-only queries (IsOpen, GetState, GetAllStates, GetSnapshot) in ICircuitBreakerObserver
- [ ] Write operations (ForceOpenAsync, ResetAsync) in ICircuitBreakerOperator
- [ ] DI registrations separate
- [ ] Existing usages updated

## Notes

PR #194 code review finding. Low urgency but prevents future privilege creep.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-23 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
