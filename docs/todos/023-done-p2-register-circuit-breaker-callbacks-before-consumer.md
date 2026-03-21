---
status: done
priority: p2
issue_id: "023"
tags: ["code-review","messaging","race-condition"]
dependencies: []
---

# Register circuit-breaker callbacks before consumer tasks start

## Problem Statement

ConsumerRegister starts the per-group consumer tasks before calling RegisterGroupCallbacks on the circuit breaker. If a fast failure trips the breaker before the callbacks are registered, onPause/onResume are null and the live consumers never get paused or resumed.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:187
- **Impact:** An early circuit trip can open the breaker without pausing the active transport clients.

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

_To be determined during triage._

## Acceptance Criteria

- [ ] Group callbacks are registered before any consumer loop can report failures
- [ ] An early trip still pauses and resumes the group correctly

## Notes

From PR #194 review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: pending → done
