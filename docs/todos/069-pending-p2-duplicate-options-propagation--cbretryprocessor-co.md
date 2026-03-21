---
status: pending
priority: p2
issue_id: "069"
tags: ["code-review","architecture"]
dependencies: []
---

# Duplicate options propagation — CB/RetryProcessor configured 3x in Setup.cs

## Problem Statement

CircuitBreaker and RetryProcessor options copied property-by-property twice: into IOptions<MessagingOptions> lambda (lines 183-192) AND standalone IOptions<CircuitBreakerOptions> (lines 202-215). New properties require 3 edits. MessagingOptions.CircuitBreaker is read nowhere except this copy loop.

## Findings

- **Location:** Setup.cs:155-215
- **Risk:** Medium — maintenance trap, copy divergence
- **Discovered by:** pragmatic-dotnet-reviewer, code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Remove sub-properties from MessagingOptions or add CopyFrom helper. Only the standalone Configure<T> registrations are needed by DI consumers.

## Acceptance Criteria

- [ ] CircuitBreakerOptions not duplicated across registrations
- [ ] New property additions require at most 2 edits (class + config lambda)

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
