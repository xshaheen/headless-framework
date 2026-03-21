---
status: done
priority: p2
issue_id: "119"
tags: ["code-review","dotnet","messaging","security"]
dependencies: []
---

# Sanitize groupName in CircuitBreakerStateManager logs and add null/length guard to ResetAsync

## Problem Statement

groupName is logged unsanitized across all state transition methods in CircuitBreakerStateManager. ResetAsync (public API) has no null guard — throws NullReferenceException on null input — and no length cap, allowing 1 MB strings in log entries. An attacker controlling broker headers or the management API can inject log content.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:104,325,389,458,495,539,562,566,618
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs:41
- **Risk:** OWASP A09 log injection; NullReferenceException on null groupName in ResetAsync
- **Discovered by:** compound-engineering:review:security-sentinel

## Proposed Solutions

### Add ArgumentNullException.ThrowIfNull + length cap (>512) at ResetAsync entry; ensure all log structured properties use sanitized groupName
- **Pros**: Consistent with _SanitizeGroupName pattern already in file
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add null guard and 512-char length cap to ResetAsync. Ensure groupName in log structured properties is always the sanitized value.

## Acceptance Criteria

- [ ] ResetAsync throws ArgumentNullException for null
- [ ] ResetAsync throws for groupName.Length > 512
- [ ] All log structured properties use sanitized groupName

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
