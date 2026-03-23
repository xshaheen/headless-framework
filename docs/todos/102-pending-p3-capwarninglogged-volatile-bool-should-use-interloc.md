---
status: pending
priority: p3
issue_id: "102"
tags: ["code-review","dotnet","thread-safety"]
dependencies: []
---

# _capWarningLogged volatile bool should use Interlocked.CompareExchange

## Problem Statement

The check-then-set on _capWarningLogged is not atomic. Two threads can both see false and both log. Inconsistent with rest of class which uses Lock or Interlocked.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:43-44,658-669
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer, compound-engineering:review:code-simplicity-reviewer, compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Change to int field + Interlocked.CompareExchange(ref _capWarningLogged, 1, 0) == 0.

## Acceptance Criteria

- [ ] _capWarningLogged uses Interlocked.CompareExchange pattern

## Notes

Source: Code review

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
