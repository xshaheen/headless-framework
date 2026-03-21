---
status: ready
priority: p1
issue_id: "047"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Fix EscalationLevel increment order and documentation clarity

## Problem Statement

In CircuitBreakerStateManager._TransitionToOpen, _GetOpenDuration(state) is called BEFORE state.EscalationLevel++ increments the level. This means the first trip uses EscalationLevel=0 (30s), then EscalationLevel becomes 1 for the next trip. The comment says Zero-based escalation level. Incremented each time the circuit opens — but the effective semantics are this value is the level for the NEXT opening since we increment after reading. This is confusing; a maintainer who reads increment each time circuit opens will expect the increment to happen at the start of open, not at the end. The current behavior also differs from the PR summary which implies trip N uses escalation level N-1.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:181-183
- **Risk:** Medium - confusing maintenance trap, future maintainer will likely invert order and break escalation
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Invert order: increment first, adjust _GetOpenDuration to use current level
- **Pros**: Code reads as documented: level increments ON each open
- **Cons**: Changes durations: first trip now 60s instead of 30s
- **Effort**: Small
- **Risk**: Medium

### Keep current order, update comment to be precise
- **Pros**: No behavioral change
- **Cons**: Still slightly confusing
- **Effort**: Small
- **Risk**: Low

### Invert order + rename field to NextEscalationLevel
- **Pros**: Both code and name match current semantics precisely
- **Cons**: Field rename
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Invert the increment order (increment before use) so the code matches the documented intent. Update _GetOpenDuration accordingly so the escalation sequence remains 30s, 60s, 120s, 240s as designed. Update the XML doc on EscalationLevel to be explicit.

## Acceptance Criteria

- [ ] EscalationLevel increment happens before _GetOpenDuration reads it (or comment precisely explains current semantics)
- [ ] First open duration is still 30s (or explicitly documented as 60s if behavior changes)
- [ ] Tests verify escalation sequence

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
