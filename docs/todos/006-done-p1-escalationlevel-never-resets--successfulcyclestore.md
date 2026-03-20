---
status: done
priority: p1
issue_id: "006"
tags: ["code-review","messaging","circuit-breaker","dotnet"]
dependencies: []
---

# EscalationLevel never resets — SuccessfulCyclesToResetEscalation option is dead code

## Problem Statement

In _TransitionToClosed, state.SuccessfulCyclesAfterClose is incremented but there is no code that checks it against _options.SuccessfulCyclesToResetEscalation and resets EscalationLevel to zero. The documented and configurable option does nothing. EscalationLevel grows unbounded: at level ~53 (with 30s base), Math.Pow(2,53)*30 overflows double, TimeSpan.FromSeconds(Infinity) throws OverflowException inside _TransitionToOpen under the group lock, leaving GroupCircuitState corrupt.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:_TransitionToClosed
- **Risk:** Critical — crash after ~53 circuit trips in long-lived process
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, security-sentinel, performance-oracle, architecture-strategist

## Proposed Solutions

### Add reset check in _TransitionToClosed
- **Pros**: Minimal change, exactly what the option documents
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add after state.SuccessfulCyclesAfterClose++: if (state.SuccessfulCyclesAfterClose >= _options.SuccessfulCyclesToResetEscalation) { state.EscalationLevel = 0; state.SuccessfulCyclesAfterClose = 0; }. Also add a safety cap in _GetOpenDuration: var safeLevel = Math.Min(state.EscalationLevel, 62).

## Acceptance Criteria

- [ ] EscalationLevel resets to 0 after N=SuccessfulCyclesToResetEscalation consecutive closed cycles
- [ ] SuccessfulCyclesAfterClose resets to 0 when escalation resets
- [ ] _GetOpenDuration caps EscalationLevel before Math.Pow to prevent double overflow
- [ ] Unit test verifies next trip after reset uses base open duration

## Notes

PR #194 review. Security-sentinel identified potential OverflowException at extreme levels.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-20 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
