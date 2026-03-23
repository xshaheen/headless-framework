---
status: done
priority: p2
issue_id: "095"
tags: ["code-review","messaging","quality"]
dependencies: []
---

# Remove misleading NotNull() validator rule for IsTransientException delegate

## Problem Statement

CircuitBreakerOptionsValidator has `RuleFor(x => x.IsTransientException).NotNull()` but the delegate is always set during AddHeadlessMessaging — it will never be null at validation time. The rule provides false assurance and is misleading about what the validator actually protects. The real runtime protection is the try/catch around predicate invocation in CircuitBreakerStateManager.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerOptions.cs:~71
- **Problem:** NotNull() rule on delegate always passes — provides no protection
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Remove the NotNull() rule for IsTransientException
- **Pros**: Removes misleading validator rule
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove `RuleFor(x => x.IsTransientException).NotNull()` from the validator. Add a code comment in the validator explaining that predicate validation is handled at runtime by the try/catch in CircuitBreakerStateManager.

## Acceptance Criteria

- [ ] Misleading NotNull() rule removed
- [ ] Comment explaining runtime predicate protection added
- [ ] Validator tests updated

## Notes

PR #194 code review finding.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-23 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
