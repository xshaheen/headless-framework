---
status: pending
priority: p3
issue_id: "076"
tags: ["code-review","simplicity","messaging"]
dependencies: []
---

# Eliminate ConsumerCircuitBreakerRegistry class — fold into MessagingOptions

## Problem Statement

ConsumerCircuitBreakerRegistry is a 93-line class that wraps ConcurrentDictionary<string, ConsumerCircuitBreakerOptions> with 4 delegating methods and inline validation. It has exactly 3 callers. This is an over-abstracted indirection that adds a DI registration, a file, and a class for what amounts to three lines of dictionary usage.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerRegistry.cs
- **Problem:** 93-line wrapper over ConcurrentDictionary with 3 callers — YAGNI
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

### Replace with ConcurrentDictionary<string, ConsumerCircuitBreakerOptions> on MessagingOptions
- **Pros**: Removes entire class and DI registration
- **Cons**: Inline validation needs a home
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Replace ConsumerCircuitBreakerRegistry with `internal ConcurrentDictionary<string, ConsumerCircuitBreakerOptions> CircuitBreakerOverrides` on MessagingOptions. Move validation to a static helper on CircuitBreakerOptions or inline in ConsumerBuilder.WithCircuitBreaker.

## Acceptance Criteria

- [ ] ConsumerCircuitBreakerRegistry class removed
- [ ] Equivalent functionality via dictionary on MessagingOptions
- [ ] All 3 call sites updated
- [ ] Tests updated

## Notes

PR #194 code review finding. Low-risk simplification.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
