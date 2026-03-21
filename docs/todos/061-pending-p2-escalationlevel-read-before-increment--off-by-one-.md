---
status: pending
priority: p2
issue_id: "061"
tags: ["code-review","dotnet"]
dependencies: []
---

# EscalationLevel read-before-increment — off-by-one and misleading logs

## Problem Statement

_GetOpenDuration reads state.EscalationLevel THEN state.EscalationLevel++ increments it. First trip always uses level 0. Log message at line 397 shows post-increment value — 'escalation: 1' on first trip, misleading operators. Confirmed by 4 review agents.

## Findings

- **Location:** CircuitBreakerStateManager.cs:378-379,397
- **Risk:** Medium — misleading logs + off-by-one semantics
- **Discovered by:** pragmatic-dotnet-reviewer, code-simplicity-reviewer, security-sentinel, learnings-researcher

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Increment EscalationLevel before calling _GetOpenDuration. First open = level 1. Adjust _GetOpenDuration: baseDuration * 2^(level-1). Log pre-increment value.

## Acceptance Criteria

- [ ] EscalationLevel incremented before duration computation
- [ ] Log message shows level used for current duration
- [ ] First open uses base duration (level 1 → 2^0 = 1x)

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
