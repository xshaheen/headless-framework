---
status: pending
priority: p3
issue_id: "033"
tags: ["code-review","simplification"]
dependencies: []
---

# Consider merging ICircuitBreakerMonitor/ICircuitBreakerStateManager interfaces

## Problem Statement

ICircuitBreakerStateManager inherits ICircuitBreakerMonitor. Both have exactly one implementation. The split exists for read-only vs write access, but the DI registration uses a factory cast (sp => (ICircuitBreakerMonitor)sp.GetRequiredService<ICircuitBreakerStateManager>()). This adds ~50 LOC of boilerplate for an abstraction that will never have a second implementation.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs, ICircuitBreakerStateManager.cs
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Keep ICircuitBreakerMonitor as the public interface. Make ICircuitBreakerStateManager internal without inheritance — or merge entirely.

## Acceptance Criteria

- [ ] Simplified interface hierarchy
- [ ] DI alias removed or simplified

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
