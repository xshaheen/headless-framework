---
status: pending
priority: p2
issue_id: "085"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# ResetAsync silently no-ops for unknown group names — return bool result

## Problem Statement

ICircuitBreakerMonitor.ResetAsync returns ValueTask (void) and silently does nothing for unrecognized group names. An agent performing remediation by calling ResetAsync on a list of groups cannot verify which resets had effect vs. which found nothing (wrong name, typo, etc.).

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs (ResetAsync), CircuitBreakerStateManager.cs (~line 285)
- **Risk:** Silent failure of agent remediation workflows
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

### Change ResetAsync to return ValueTask<bool> (true = reset performed, false = group not found)
- **Pros**: Actionable feedback for callers
- **Cons**: Breaking API change on public interface
- **Effort**: Small
- **Risk**: Low

### Add XML doc note that unknown group names are silently ignored
- **Pros**: No API change
- **Cons**: Does not actually fix the discoverability gap
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change ResetAsync signature to ValueTask<bool> ResetAsync(string groupName, CancellationToken ct = default) returning true when a reset was performed. Update implementation and all callers.

## Acceptance Criteria

- [ ] ResetAsync returns bool indicating whether reset had effect
- [ ] False returned for unknown group names
- [ ] XML doc updated
- [ ] Test verifies false returned for unknown group

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
