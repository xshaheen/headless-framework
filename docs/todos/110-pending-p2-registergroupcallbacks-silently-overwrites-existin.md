---
status: pending
priority: p2
issue_id: "110"
tags: ["code-review","dotnet","defensiveness"]
dependencies: []
---

# RegisterGroupCallbacks silently overwrites existing callbacks

## Problem Statement

CircuitBreakerStateManager.RegisterGroupCallbacks silently replaces previously registered pause/resume callbacks for a group. If the same group name is registered twice (misconfiguration), the first set of transports will no longer receive pause/resume signals. This is inconsistent with ConsumerCircuitBreakerRegistry.Register which throws on duplicate.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:66-76
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Throw InvalidOperationException if callbacks are already registered for the group.

## Acceptance Criteria

- [ ] RegisterGroupCallbacks throws on duplicate registration

## Notes

Source: Code review

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
