---
status: pending
priority: p3
issue_id: "044"
tags: ["code-review","conventions"]
dependencies: []
---

# Replace ArgumentOutOfRangeException with Headless.Checks guards in ConsumerCircuitBreakerRegistry

## Problem Statement

ConsumerCircuitBreakerRegistry._ValidateOptions (lines 84-98) throws ArgumentOutOfRangeException directly. Project convention requires Headless.Checks guards (Argument.*, Ensure.*).

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerRegistry.cs:84-98
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Replace with Argument.IsGreaterThan / Argument.IsGreaterThanOrEqualTo guards.

## Acceptance Criteria

- [ ] No direct ArgumentOutOfRangeException throws
- [ ] Uses Headless.Checks Argument.* guards

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
