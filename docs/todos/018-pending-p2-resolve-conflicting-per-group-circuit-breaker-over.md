---
status: pending
priority: p2
issue_id: "018"
tags: ["code-review","dotnet","architecture","quality"]
dependencies: []
---

# Resolve conflicting per-group circuit breaker overrides and expose them for inspection

## Problem Statement

The new override registry is keyed only by consumer group, so multiple handlers sharing a group silently overwrite each other and the last registration wins. The effective override is also stored only in an internal registry, so agents and tooling that inspect registered consumers cannot discover which groups have custom breaker settings or whether a breaker is disabled. Together this makes shared-group configuration conflicts hard to detect and reason about.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerRegistry.cs:16
- **Location:** src/Headless.Messaging.Core/ConsumerBuilder.cs:103
- **Location:** src/Headless.Messaging.Core/ServiceCollectionConsumerBuilder.cs:87
- **Location:** src/Headless.Messaging.Core/ConsumerMetadata.cs:18
- **Risk:** Medium - shared groups can silently receive the wrong override and tooling cannot inspect effective settings
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer, agent-native-reviewer

## Proposed Solutions

### Make overrides explicitly group-scoped and reject conflicting registrations
- **Pros**: Clear behavior and easy to document
- **Cons**: May require a small breaking change for ambiguous setups
- **Effort**: Medium
- **Risk**: Low

### Key overrides by full consumer identity and surface effective settings publicly
- **Pros**: Most precise model and best inspection story
- **Cons**: More design work and API surface change
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Choose one scope model explicitly and make it inspectable. Minimum bar: reject conflicting same-group overrides and expose effective breaker settings through a public metadata or monitor surface.

## Acceptance Criteria

- [ ] Conflicting overrides for the same group are either rejected or have documented deterministic behavior
- [ ] Effective per-group or per-consumer breaker settings can be inspected through a public API
- [ ] Tests cover shared-group conflicts and discovery of effective settings

## Notes

Discovered during PR #194 code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
