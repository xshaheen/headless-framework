---
status: done
priority: p2
issue_id: "040"
tags: ["code-review","correctness"]
dependencies: []
---

# Fix 512 vs 256 limit mismatch between ResetAsync and _SanitizeGroupName

## Problem Statement

ResetAsync (CircuitBreakerStateManager.cs:362) validates groupName.Length <= 512, but _SanitizeGroupName truncates to 256 characters. A 400-character group name passes ResetAsync validation but would be truncated in _SanitizeGroupName — the truncated key won't match the stored dictionary key, causing ResetAsync to return false for a valid group.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:362 vs IConsumerRegister.cs:512-570
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Align both limits to the same value (256 or 512). If LogSanitizer.Sanitize is extended with maxLength (per P2 log injection todo), use that consistently.

## Acceptance Criteria

- [ ] ResetAsync validation limit matches truncation limit used for dictionary keys
- [ ] No silent key mismatch between input validation and stored keys

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-22 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
