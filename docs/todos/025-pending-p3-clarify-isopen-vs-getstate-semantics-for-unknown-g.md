---
status: pending
priority: p3
issue_id: "025"
tags: ["code-review","api-design"]
dependencies: []
---

# Clarify IsOpen vs GetState semantics for unknown groups

## Problem Statement

IsOpen returns false for unknown groups. GetState returns null. These look like the same question ('is this group healthy?') but give different answers. An agent writing if(GetState('payments') == null) gets different behavior than if(!IsOpen('payments')).

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:273-282,306-315
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Document the asymmetry clearly in ICircuitBreakerMonitor XML docs. IsOpen = advisory hint. GetState = registration check.

## Acceptance Criteria

- [ ] XML docs explicitly document semantic difference
- [ ] Example usage in doc comments

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
