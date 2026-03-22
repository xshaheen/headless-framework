---
status: pending
priority: p3
issue_id: "069"
tags: ["code-review","quality"]
dependencies: []
---

# RetryProcessorOptionsValidator validates MaxPollingInterval when AdaptivePolling=false — misleading validation

## Problem Statement

RetryProcessorOptionsValidator validates MaxPollingInterval and CircuitOpenRateThreshold regardless of whether AdaptivePolling is enabled. When AdaptivePolling = false, both properties are unused. An operator who disables adaptive polling and sets an invalid MaxPollingInterval gets a validation error on a property that has no effect on runtime behavior.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/RetryProcessorOptionsValidator.cs:17-25
- **Discovered by:** strict-dotnet-reviewer (P3.6)

## Proposed Solutions

### Wrap MaxPollingInterval and CircuitOpenRateThreshold rules in When(x => x.AdaptivePolling)
- **Pros**: Only validates properties that are actually used
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Use FluentValidation's 'When(x => x.AdaptivePolling)' condition for the MaxPollingInterval and CircuitOpenRateThreshold rules.

## Acceptance Criteria

- [ ] MaxPollingInterval validation only applies when AdaptivePolling = true
- [ ] CircuitOpenRateThreshold validation only applies when AdaptivePolling = true
- [ ] Tests verify no validation error when AdaptivePolling = false with any MaxPollingInterval value

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
