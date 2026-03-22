---
status: pending
priority: p3
issue_id: "036"
tags: ["code-review","simplification"]
dependencies: []
---

# Eliminate ConsumerCircuitBreakerRegistration by resolving group at builder time

## Problem Statement

ConsumerCircuitBreakerRegistration is a DI marker record that defers group name resolution. Setup.cs scans for it and re-resolves via _DiscoverCircuitBreakerRegistrationsFromDI (~35 lines). The group name is already available at ConsumerBuilder.Build() time. This adds ~90 LOC of deferred-registration complexity.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerRegistration.cs, Setup.cs:239-277
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Register directly into ConsumerCircuitBreakerRegistry during builder Build() instead of deferring. Delete ConsumerCircuitBreakerRegistration and _DiscoverCircuitBreakerRegistrationsFromDI.

## Acceptance Criteria

- [ ] ConsumerCircuitBreakerRegistration record deleted
- [ ] Group resolved at Build() time
- [ ] ~90 LOC removed

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
