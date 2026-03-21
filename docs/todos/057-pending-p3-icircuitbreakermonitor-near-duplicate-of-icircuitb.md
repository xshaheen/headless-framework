---
status: pending
priority: p3
issue_id: "057"
tags: ["code-review","architecture"]
dependencies: []
---

# ICircuitBreakerMonitor near-duplicate of ICircuitBreakerStateManager

## Problem Statement

Two-interface split (monitor = read-only, state manager = full) adds a file, an interface, a DI cast registration. Nothing in the codebase injects ICircuitBreakerMonitor. IsOpen declared on both interfaces. Permanent API surface hard to remove later.

## Findings

- **Location:** ICircuitBreakerMonitor.cs, ICircuitBreakerStateManager.cs, Setup.cs:144
- **Discovered by:** code-simplicity-reviewer, agent-native-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Collapse into single interface. If read-only concern is genuine, expose minimal methods on public interface and keep mutation methods internal.

## Acceptance Criteria

- [ ] Interface surface simplified or read-only split justified with consumer

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
