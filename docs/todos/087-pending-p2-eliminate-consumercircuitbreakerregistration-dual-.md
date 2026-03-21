---
status: pending
priority: p2
issue_id: "087"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Eliminate ConsumerCircuitBreakerRegistration dual-path staging record

## Problem Statement

There are two separate code paths for registering per-consumer circuit breaker options: (1) MessagingOptions.Subscribe with WithCircuitBreaker writes directly to MessagingOptions.CircuitBreakerRegistry, (2) services.AddConsumer with WithCircuitBreaker creates ConsumerCircuitBreakerRegistration DI singletons that Setup._DiscoverCircuitBreakerRegistrationsFromDI then reads and copies. The staging record exists only to bridge two registration paths. Adding a new property to ConsumerCircuitBreakerOptions requires updating 3 places.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerRegistration.cs (entire file), src/Headless.Messaging.Core/Setup.cs (_DiscoverCircuitBreakerRegistrationsFromDI, ~line 4441)
- **Risk:** Maintenance burden — property additions require 3-file updates
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

### Have ServiceCollectionConsumerBuilder resolve ConsumerCircuitBreakerRegistry from DI and write directly
- **Pros**: Eliminates staging record and DI-scan loop (~80 LOC removed)
- **Cons**: Requires ConsumerCircuitBreakerRegistry registered early in DI (before AddHeadlessMessaging)
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Register ConsumerCircuitBreakerRegistry as a DI singleton at AddConsumer time, then have ServiceCollectionConsumerBuilder resolve and write directly. Remove ConsumerCircuitBreakerRegistration.cs and _DiscoverCircuitBreakerRegistrationsFromDI.

## Acceptance Criteria

- [ ] ConsumerCircuitBreakerRegistration.cs deleted
- [ ] _DiscoverCircuitBreakerRegistrationsFromDI removed from Setup.cs
- [ ] Both registration paths (Subscribe + AddConsumer) write to same registry
- [ ] All existing tests pass

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
