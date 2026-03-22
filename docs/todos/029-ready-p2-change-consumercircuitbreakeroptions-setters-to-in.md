---
status: ready
priority: p2
issue_id: "029"
tags: ["code-review","api-design"]
dependencies: []
---

# Change ConsumerCircuitBreakerOptions setters to init

## Problem Statement

ConsumerCircuitBreakerOptions uses public setters (set) for Enabled, FailureThreshold, OpenDuration, and IsTransientException. These values are configured at startup via WithCircuitBreaker(cb => { ... }) and then frozen. The public setters invite runtime mutation that has no effect, creating a pit of failure for framework consumers.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerOptions.cs
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Change all setters to init. The Action<ConsumerCircuitBreakerOptions> configure delegate pattern uses object initializer syntax which works with init.

## Acceptance Criteria

- [ ] All properties use init instead of set
- [ ] WithCircuitBreaker configure delegate still works
- [ ] No runtime mutation possible after configuration

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
