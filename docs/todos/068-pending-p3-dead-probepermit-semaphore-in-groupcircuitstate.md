---
status: pending
priority: p3
issue_id: "068"
tags: ["code-review","quality"]
dependencies: []
---

# Dead _probePermit semaphore in GroupCircuitState

## Problem Statement

GroupCircuitState._probePermit (SemaphoreSlim) and _TryReleaseProbeSemaphore are dead code. Nothing acquires the semaphore — all releases are no-ops. Identified in prior review as P1.

## Findings

- **Location:** CircuitBreakerStateManager.cs (GroupCircuitState)
- **Discovered by:** learnings-researcher (prior review)

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Remove _probePermit and _TryReleaseProbeSemaphore, or wire up acquisition if the semaphore serves a purpose.

## Acceptance Criteria

- [ ] Dead semaphore code removed or properly wired

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
