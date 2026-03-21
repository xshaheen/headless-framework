---
status: done
priority: p1
issue_id: "012"
tags: ["code-review","dotnet","quality","architecture"]
dependencies: []
---

# Make circuit breaker configuration API usable

## Problem Statement

The new public configuration surface for circuit breakers is not usable by normal consumers. `MessagingOptions.CircuitBreaker` and `ConsumerCircuitBreakerOptions` are configured through mutable `Action<TOptions>` callbacks, but their properties are declared with `init` accessors. As a result, examples such as `options.CircuitBreaker.FailureThreshold = 5;` and `.WithCircuitBreaker(cb => cb.FailureThreshold = 3)` do not compile, so the feature ships with a broken public API.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerOptions.cs:19
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerOptions.cs:17
- **Location:** src/Headless.Messaging.Core/ConsumerBuilder.cs:87
- **Location:** src/Headless.Messaging.Core/ServiceCollectionConsumerBuilder.cs:79
- **Risk:** High - public API advertised in README and builder methods cannot be used by consumers
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer

## Proposed Solutions

### Switch option properties to set accessors
- **Pros**: Matches existing MessagingOptions pattern and current Action<TOptions> API
- **Cons**: Gives up init-only immutability
- **Effort**: Small
- **Risk**: Low

### Redesign builders to replace whole immutable option objects
- **Pros**: Preserves immutability semantics
- **Cons**: Larger API redesign and more breaking surface
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Use normal set accessors for these option properties so the existing Action<TOptions> configuration API works as documented.

## Acceptance Criteria

- [ ] Consumers can configure global circuit breaker options inside AddHeadlessMessaging without compiler errors
- [ ] Consumers can configure per-consumer overrides inside WithCircuitBreaker without compiler errors
- [ ] README/examples compile against the public API
- [ ] Tests cover both global and per-consumer configuration

## Notes

Discovered during PR #194 code review

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
