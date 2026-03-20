---
status: pending
priority: p1
issue_id: "003"
tags: ["code-review","messaging","circuit-breaker","dotnet"]
dependencies: []
---

# Per-consumer WithCircuitBreaker() overrides silently ignored at runtime

## Problem Statement

ConsumerCircuitBreakerOptions registered via .WithCircuitBreaker(cb => cb.FailureThreshold = 3) or cb.Enabled = false are stored in ConsumerCircuitBreakerRegistry but CircuitBreakerStateManager never injects or reads the registry. ReportFailureAsync always uses global _options.FailureThreshold and CircuitBreakerDefaults.IsTransient. Per-consumer Enabled=false has no effect — the circuit breaker still fires. The entire WithCircuitBreaker() public API is a no-op at runtime.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:ReportFailureAsync
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerRegistry.cs
- **Risk:** Critical — documented public API does nothing
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, performance-oracle, architecture-strategist, code-simplicity-reviewer

## Proposed Solutions

### Inject ConsumerCircuitBreakerRegistry into CircuitBreakerStateManager
- **Pros**: Complete fix, correct behavior
- **Cons**: Small amount of work
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Inject ConsumerCircuitBreakerRegistry into CircuitBreakerStateManager and resolve per-group options in ReportFailureAsync: FailureThreshold, IsTransientException, OpenDuration, and Enabled override global values.

## Acceptance Criteria

- [ ] cb.Enabled = false prevents circuit from tripping for that group
- [ ] cb.FailureThreshold = 3 overrides global threshold for that group
- [ ] cb.IsTransientException = myPredicate is used instead of CircuitBreakerDefaults for that group
- [ ] cb.OpenDuration overrides global open duration for that group
- [ ] Unit test verifies per-consumer override takes effect

## Notes

PR #194 review. All 5 code reviewers independently flagged this.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
