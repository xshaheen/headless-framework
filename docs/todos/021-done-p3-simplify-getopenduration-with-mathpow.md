---
status: done
priority: p3
issue_id: "021"
tags: ["code-review","simplification"]
dependencies: []
---

# Simplify _GetOpenDuration with Math.Pow

## Problem Statement

_GetOpenDuration (CircuitBreakerStateManager.cs:748-761) uses 1L << safeLevel with a >= 53 guard that is dead code for any realistic MaxOpenDuration. The bit-shift comment is also technically wrong — it's the multiplication result that loses precision, not the shift itself. Math.Pow handles overflow naturally.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:748-761
- **Discovered by:** code-simplicity-reviewer, strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Replace with: var scaled = state.EffectiveOpenDuration.TotalSeconds * Math.Pow(2, Math.Max(0, state.EscalationLevel - 1)); return TimeSpan.FromSeconds(Math.Min(scaled, _options.MaxOpenDuration.TotalSeconds));

## Acceptance Criteria

- [ ] Dead branches removed
- [ ] Comment accuracy fixed
- [ ] Same behavior for all realistic inputs

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
