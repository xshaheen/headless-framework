---
status: done
priority: p1
issue_id: "064"
tags: ["code-review","security"]
dependencies: []
---

# CircuitBreakerOptionsValidator missing MaxOpenDuration upper bound — escalation-inflation DoS

## Problem Statement

The escalation-inflation attack: an attacker who can inject exactly FailureThreshold transient failures per cycle, wait for HalfOpen, then inject one more during the probe, will increment EscalationLevel once per cycle. After 6 cycles the computed open duration saturates at MaxOpenDuration (default 240s). MaxOpenDuration itself has no upper bound in the validator — an operator can set it to TimeSpan.MaxValue, making consumer groups permanently paused under sustained attack. Additionally there is no MaxEscalationLevel option, so operators cannot limit how aggressively the open duration ratchets up.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerOptionsValidator.cs
- **Risk:** Sustained attacker can hold consumer groups paused at MaxOpenDuration indefinitely
- **Discovered by:** security-sentinel (P2)
- **Secondary:** No MaxEscalationLevel config to cap escalation independent of MaxOpenDuration

## Proposed Solutions

### Add MaxOpenDuration upper bound (e.g. 24 hours) to CircuitBreakerOptionsValidator
- **Pros**: Prevents misconfiguration from making outages permanent
- **Cons**: Breaks configs that explicitly set MaxOpenDuration > 24h (unlikely but possible)
- **Effort**: Small
- **Risk**: Low

### Add MaxEscalationLevel option
- **Pros**: Operators can cap escalation independent of duration limits
- **Cons**: Adds configuration surface
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add MaxOpenDuration <= TimeSpan.FromDays(1) rule to CircuitBreakerOptionsValidator. Optionally add MaxEscalationLevel property (default: 4) so escalation caps at 240s even with custom base durations.

## Acceptance Criteria

- [x] CircuitBreakerOptionsValidator rejects MaxOpenDuration > 24 hours
- [x] Validator test for upper bound
- [x] XML doc updated to document the 24h cap

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
