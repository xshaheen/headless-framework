---
status: pending
priority: p2
issue_id: "022"
tags: ["code-review","security","duplication"]
dependencies: []
---

# Fix log injection: apply LogSanitizer consistently and remove _SanitizeGroupName duplicate

## Problem Statement

LogSanitizer.Sanitize is applied selectively — only in ResetAsync and _GetOrAddState. Seven other log sites in CircuitBreakerStateManager pass raw groupName (lines 115, 539, 610, 617, 662, 681, 740). Additionally, IConsumerRegister._SanitizeGroupName (lines 512-570) is a 55-line duplicate of LogSanitizer.Sanitize with extra truncation. The two implementations will silently diverge.

## Findings

- **Log injection sites:** CircuitBreakerStateManager.cs:115,539,610,617,662,681,740
- **Duplicate code:** IConsumerRegister.cs:512-570 (~55 LOC)
- **Discovered by:** security-sentinel, code-simplicity-reviewer

## Proposed Solutions

### Extend LogSanitizer.Sanitize with maxLength parameter, delete _SanitizeGroupName
- **Pros**: Single source of truth, eliminates 55 LOC
- **Cons**: Minor API change to internal method
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add optional maxLength parameter to LogSanitizer.Sanitize(string? value, int maxLength = int.MaxValue). Delete _SanitizeGroupName. Apply LogSanitizer.Sanitize consistently at all 7 unsanitized log sites.

## Acceptance Criteria

- [ ] All groupName log sites use LogSanitizer.Sanitize
- [ ] _SanitizeGroupName method deleted
- [ ] LogSanitizer.Sanitize accepts optional maxLength parameter
- [ ] No raw groupName in structured log parameters

## Notes

groupName derives from message.Origin.GetGroup() which reads transport headers — attacker-controlled in some scenarios.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
