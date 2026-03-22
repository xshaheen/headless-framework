---
status: ready
priority: p3
issue_id: "046"
tags: ["code-review","async"]
dependencies: []
---

# Add IAsyncDisposable to CircuitBreakerStateManager

## Problem Statement

CircuitBreakerStateManager only implements IDisposable despite using DisposeAsync() for timers in ReportFailureAsync and ResetAsync. The synchronous Dispose() cannot wait for in-flight callbacks to complete. IAsyncDisposable would allow proper async cleanup.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:22
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Implement both IDisposable and IAsyncDisposable. DisposeAsync awaits timer disposal and in-flight callbacks.

## Acceptance Criteria

- [ ] IAsyncDisposable implemented
- [ ] DisposeAsync properly awaits timer disposal

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
