---
status: done
priority: p2
issue_id: "122"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Add validation for ConsumerCircuitBreakerOptions per-consumer overrides

## Problem Statement

CircuitBreakerOptions has a validator. ConsumerCircuitBreakerOptions (per-group override via .WithCircuitBreaker()) has none. FailureThreshold=0 trips the circuit after exactly one failure. OpenDuration=TimeSpan.Zero produces a zero-delay timer immediately transitioning to HalfOpen, effectively disabling the circuit. No validation catches either case.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerOptions.cs
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerRegistry.cs:29-40
- **Risk:** Silent misconfiguration; circuit trips every message or is disabled
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer

## Proposed Solutions

### Add ConsumerCircuitBreakerOptionsValidator : AbstractValidator<ConsumerCircuitBreakerOptions> per project conventions
- **Pros**: Consistent with all other validators in the project
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add ConsumerCircuitBreakerOptionsValidator validating nullable overrides when set (FailureThreshold > 0 if provided, OpenDuration > Zero if provided).

## Acceptance Criteria

- [ ] FailureThreshold=0 rejected
- [ ] OpenDuration=TimeSpan.Zero rejected
- [ ] Null overrides (unset) still accepted

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
