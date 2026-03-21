---
status: pending
priority: p2
issue_id: "065"
tags: ["code-review","dotnet"]
dependencies: []
---

# Options validation uses IValidateOptions not AbstractValidator — convention violation

## Problem Statement

ConsumerCircuitBreakerOptions and RetryProcessorOptions are registered with IValidateOptions<T> but project convention mandates FluentValidation AbstractValidator<T> via services.Configure<TOption, TValidator>() or services.AddOptions<TOption, TValidator>(). Options snapshotted via Options.Create() bypass IValidateOptions entirely — validation never fires.

## Findings

- **Location:** CircuitBreakerOptionsValidator.cs, RetryProcessorOptionsValidator.cs, Setup.cs
- **Risk:** Medium — validation silently never runs
- **Discovered by:** learnings-researcher

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Replace IValidateOptions<T> with AbstractValidator<T>. Register via services.AddOptions<T, TValidator>() per project convention.

## Acceptance Criteria

- [ ] All options validators use AbstractValidator<T>
- [ ] Registered via DI pipeline (not manual ValidateAndThrow)
- [ ] Validation fires at startup via ValidateOnStart

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
