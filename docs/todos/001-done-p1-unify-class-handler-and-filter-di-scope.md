---
status: done
priority: p1
issue_id: "001"
tags: ["code-review","architecture","dotnet","quality"]
dependencies: []
---

# Unify class handler and filter DI scope

## Problem Statement

The new shared consume pipeline creates one scope for filters and runtime delegates, but class handlers are still dispatched through CompiledMessageDispatcher, which creates a second scope. Scoped services resolved by IConsumeFilter therefore differ from the scoped services resolved by class-based IConsume<T> handlers, breaking the PR's shared execution-core contract and making filter/handler coordination nondeterministic.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumeExecutionPipeline.cs:39-68
- **Location:** src/Headless.Messaging.Core/Internal/CompiledMessageDispatcher.cs:126-150
- **Risk:** High - scoped services, ambient state, and filter behavior diverge between runtime delegates and class handlers
- **Discovered by:** strict-dotnet-reviewer / architecture review

## Proposed Solutions

### Pass the existing scope into the dispatcher
- **Pros**: Smallest behavioral change, preserves compiled dispatch
- **Cons**: Requires dispatcher API reshaping
- **Effort**: Medium
- **Risk**: Medium

### Move class-handler resolution into ConsumeExecutionPipeline
- **Pros**: Makes one scope authoritative for filters, runtime delegates, and class handlers
- **Cons**: Touches hot-path code and tests
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Use one authoritative scope for the entire consume execution path. Filters and class handlers must resolve the same scoped services.

## Acceptance Criteria

- [x] Class handlers and IConsumeFilter resolve the same scoped sentinel service instance during one delivery
- [x] Runtime delegates and class handlers both execute inside a single scope per delivery
- [x] Regression tests cover scoped DI parity across filters, runtime delegates, and class handlers

## Notes

Discovered during PR #184 review

## Work Log

### 2026-03-11 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-12 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-12 - Fixed

**By:** Codex
**Actions:**
- Added `DispatchInScopeAsync(...)` so class handlers reuse the existing consume scope
- Routed class-handler fallback in `ConsumeExecutionPipeline` through the shared scoped dispatcher path
- Added integration coverage for filter/class-handler and filter/runtime-handler scope parity

### 2026-03-12 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
