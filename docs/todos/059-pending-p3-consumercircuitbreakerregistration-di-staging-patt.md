---
status: pending
priority: p3
issue_id: "059"
tags: ["code-review","architecture"]
dependencies: []
---

# ConsumerCircuitBreakerRegistration DI staging pattern

## Problem Statement

ConsumerCircuitBreakerRegistration registered as DI singleton just to be discovered by scanning IServiceCollection later. Unusual pattern — storing transient builder data as DI singletons. Could use a simple list on MessagingOptions instead.

## Findings

- **Location:** ConsumerCircuitBreakerRegistration.cs, Setup.cs:252-280
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Store pending registrations in List on MessagingOptions. Removes record type and IServiceCollection scan.

## Acceptance Criteria

- [ ] Staging records removed from DI container

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
