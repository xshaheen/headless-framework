---
status: done
priority: p2
issue_id: "014"
tags: ["code-review","messaging","circuit-breaker","dotnet"]
dependencies: []
---

# SubscribeExecutor requires ICircuitBreakerStateManager via constructor but other registrations use GetService (nullable)

## Problem Statement

SubscribeExecutor takes ICircuitBreakerStateManager as a required constructor injection parameter, but ConsumerRegister and MessageNeedToRetryProcessor resolve it via GetService<ICircuitBreakerStateManager>() (nullable). The pattern is inconsistent. If a test harness or custom setup omits the CB registration without replacing it, SubscribeExecutor fails to resolve at DI build time rather than gracefully operating without a circuit breaker.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs:SubscribeExecutor constructor
- **Risk:** Breaks test harnesses and custom setups that don't register ICircuitBreakerStateManager
- **Discovered by:** architecture-strategist, agent-native-reviewer

## Proposed Solutions

### Change to ICircuitBreakerStateManager? (nullable) with null-guards at call sites
- **Pros**: Consistent with rest of framework
- **Cons**: Minor change
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change SubscribeExecutor constructor parameter to ICircuitBreakerStateManager? circuitBreakerStateManager = null and add null-guards before each usage, matching the pattern in ConsumerRegister.

## Acceptance Criteria

- [ ] SubscribeExecutor resolves successfully without ICircuitBreakerStateManager registered
- [ ] Circuit breaker behavior is preserved when the service is registered
- [ ] Consistent nullable pattern across all circuit-breaker-aware components

## Notes

PR #194 review.

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
