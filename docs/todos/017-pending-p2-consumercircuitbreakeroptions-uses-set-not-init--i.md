---
status: pending
priority: p2
issue_id: "017"
tags: ["code-review","messaging","dotnet","quality"]
dependencies: []
---

# ConsumerCircuitBreakerOptions uses set not init — inconsistent with framework style, mutable post-construction

## Problem Statement

ConsumerCircuitBreakerOptions.Enabled, FailureThreshold, OpenDuration, and IsTransientException all use set instead of init. The rest of the framework options (CircuitBreakerOptions, RetryProcessorOptions, etc.) use init-only properties. Mutable post-construction means code holding a reference to the options object can mutate it at runtime after registration — subtle bug surface.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerOptions.cs
- **Risk:** Post-construction mutation bug; inconsistent framework API style
- **Discovered by:** pragmatic-dotnet-reviewer, agent-native-reviewer

## Proposed Solutions

### Change set to init on all ConsumerCircuitBreakerOptions properties
- **Pros**: Consistent with framework style, immutable after construction
- **Cons**: None — configure action runs before object escapes
- **Effort**: Tiny
- **Risk**: Low


## Recommended Action

Change all property setters from set to init. The configure(options) action pattern passes the object before it escapes, so init-only is safe.

## Acceptance Criteria

- [ ] All ConsumerCircuitBreakerOptions properties use init
- [ ] WithCircuitBreaker(cb => { cb.X = y; }) still compiles
- [ ] Consistent with CircuitBreakerOptions and RetryProcessorOptions style

## Notes

PR #194 review.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
