---
status: pending
priority: p2
issue_id: "071"
tags: ["code-review","dotnet"]
dependencies: []
---

# Timer double-resume race — no generation counter for timer callbacks

## Problem Statement

Timer.Dispose does not guarantee in-flight callback cancellation. Between old timer firing and new timer creation, _OnOpenTimerElapsed can see state=Open (TransitionToOpen hasn't assigned new timer yet), transition to HalfOpen, invoke resume, then new timer fires causing double-resume.

## Findings

- **Location:** CircuitBreakerStateManager.cs:382-390
- **Risk:** Medium — double-resume race under thread pool starvation
- **Discovered by:** pragmatic-dotnet-reviewer, performance-oracle, strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add monotonic _timerGeneration counter captured in callback closure. Callback checks captured vs current generation and exits if stale. Or reuse timer via Change() instead of Dispose+new.

## Acceptance Criteria

- [ ] Timer callbacks validated against generation counter
- [ ] Stale callbacks exit early without executing resume
- [ ] No double-resume possible under any timing

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
