---
status: pending
priority: p2
issue_id: "088"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix CircuitBreakerOptions property-copy loop in Setup.cs — bind instance directly

## Problem Statement

Setup.cs manually copies all properties from options.CircuitBreaker into a second IOptions<CircuitBreakerOptions> via a Configure lambda. This means adding any new property to CircuitBreakerOptions requires updating 3 files: the class, the copy lambda, and the validator. The same pattern exists for RetryProcessorOptions.

## Findings

- **Location:** src/Headless.Messaging.Core/Setup.cs (~line 4419-4432)
- **Risk:** Maintenance burden — future property additions silently break if copy not updated
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

### Register existing instances directly: services.AddSingleton(Options.Create(options.CircuitBreaker))
- **Pros**: No copy needed, ValidateOnStart still works
- **Cons**: Minor DI registration change
- **Effort**: Small
- **Risk**: Low

### Make CircuitBreakerStateManager accept IOptions<MessagingOptions> directly
- **Pros**: Removes IOptions<CircuitBreakerOptions> entirely
- **Cons**: Couples state manager to MessagingOptions
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Use services.AddSingleton(Options.Create(options.CircuitBreaker)) and wire the validator separately. Removes the 5-property copy lambda.

## Acceptance Criteria

- [ ] No manual property-copy lambda for CircuitBreakerOptions
- [ ] ValidateOnStart still runs at startup
- [ ] All existing option validation tests pass

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
