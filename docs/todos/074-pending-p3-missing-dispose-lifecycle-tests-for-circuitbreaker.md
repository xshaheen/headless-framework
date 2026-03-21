---
status: pending
priority: p3
issue_id: "074"
tags: ["code-review","quality"]
dependencies: []
---

# Missing Dispose lifecycle tests for CircuitBreakerStateManager

## Problem Statement

No tests for Dispose. Given timer/callback race conditions (P1 finding), disposal lifecycle is critical. Need: (1) Dispose while Open — timer cancelled, resume not invoked post-dispose. (2) Dispose while callback in-flight — no ObjectDisposedException.

## Findings

- **Location:** CircuitBreakerStateManagerTests.cs (missing)
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add disposal tests with ManualResetEventSlim in resume callback to simulate in-flight callback.

## Acceptance Criteria

- [ ] Dispose-while-Open test exists
- [ ] Dispose-while-callback-in-flight test exists

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
