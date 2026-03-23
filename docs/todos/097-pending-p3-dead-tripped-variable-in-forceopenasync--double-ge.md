---
status: pending
priority: p3
issue_id: "097"
tags: ["code-review","dotnet","simplicity"]
dependencies: []
---

# Dead tripped variable in ForceOpenAsync + double _GetOpenDuration in GetSnapshot

## Problem Statement

ForceOpenAsync has a tripped bool that is always true when post-lock code runs — dead guard. GetSnapshot calls _GetOpenDuration twice for the same value instead of caching to a local.

## Findings

- **ForceOpenAsync:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:480,509-510
- **GetSnapshot:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:384-404
- **Discovered by:** compound-engineering:review:code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Remove tripped variable; call _GetOpenDuration once and reuse local.

## Acceptance Criteria

- [ ] tripped variable removed from ForceOpenAsync
- [ ] _GetOpenDuration called once in GetSnapshot

## Notes

Source: Code review

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
