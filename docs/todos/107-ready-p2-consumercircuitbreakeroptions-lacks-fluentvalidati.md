---
status: ready
priority: p2
issue_id: "107"
tags: ["code-review","dotnet","conventions"]
dependencies: []
---

# ConsumerCircuitBreakerOptions lacks FluentValidation validator

## Problem Statement

Per-group circuit breaker options are validated manually at runtime instead of via FluentValidation as required by project conventions. CircuitBreakerOptions has a validator but ConsumerCircuitBreakerOptions does not.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerOptions.cs
- **Convention:** CLAUDE.md requires FluentValidation AbstractValidator<T> in same file
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add internal sealed class ConsumerCircuitBreakerOptionsValidator : AbstractValidator<ConsumerCircuitBreakerOptions> in the same file. Validate FailureThreshold > 0, OpenDuration > TimeSpan.Zero, etc.

## Acceptance Criteria

- [ ] FluentValidation validator exists for ConsumerCircuitBreakerOptions
- [ ] Validated via DI pipeline, not manually

## Notes

Source: Code review

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
