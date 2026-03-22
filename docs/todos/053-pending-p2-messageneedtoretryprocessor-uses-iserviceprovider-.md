---
status: pending
priority: p2
issue_id: "053"
tags: ["code-review","architecture"]
dependencies: []
---

# MessageNeedToRetryProcessor uses IServiceProvider service locator — inject ICircuitBreakerStateManager directly

## Problem Statement

MessageNeedToRetryProcessor takes IServiceProvider in its constructor (IProcessor.NeedRetry.cs:54) solely to call serviceProvider.GetService<ICircuitBreakerStateManager>() at line 64. This is the service locator anti-pattern. ICircuitBreakerStateManager is registered as a singleton via TryAddSingleton in Setup.cs:142, so it can be injected directly as a nullable parameter.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:54, 64
- **Discovered by:** strict-dotnet-reviewer (P2.7), pragmatic-dotnet-reviewer (P3)

## Proposed Solutions

### Inject ICircuitBreakerStateManager? directly as optional constructor parameter
- **Pros**: Eliminates service locator, DI container handles optional resolution
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace `IServiceProvider serviceProvider` parameter with `ICircuitBreakerStateManager? circuitBreakerStateManager = null` and remove the GetService call.

## Acceptance Criteria

- [ ] IServiceProvider removed from MessageNeedToRetryProcessor constructor
- [ ] ICircuitBreakerStateManager? injected directly
- [ ] All tests still pass

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
