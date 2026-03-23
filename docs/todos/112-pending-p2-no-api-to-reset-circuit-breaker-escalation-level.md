---
status: pending
priority: p2
issue_id: "112"
tags: ["code-review","architecture","agent-native"]
dependencies: []
---

# No API to reset circuit breaker escalation level

## Problem Statement

CircuitBreakerSnapshot.EscalationLevel is observable but there is no operator API to reset it. After repeated trips, the effective open duration escalates. ResetAsync closes the circuit but does NOT reset escalation — the next trip uses elevated duration. Only automatic recovery (N successful cycles) resets it.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs
- **Discovered by:** compound-engineering:review:agent-native-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Either add ResetEscalationAsync(string groupName) to ICircuitBreakerMonitor, or explicitly document in ResetAsync XML docs and messaging.txt that escalation is NOT affected by manual reset.

## Acceptance Criteria

- [ ] Either: escalation reset API added OR ResetAsync docs explicitly state escalation is preserved

## Notes

Source: Code review

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
