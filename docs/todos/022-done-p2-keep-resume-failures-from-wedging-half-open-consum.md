---
status: done
priority: p2
issue_id: "022"
tags: ["code-review","messaging","circuit-breaker"]
dependencies: []
---

# Keep resume failures from wedging half-open consumer groups

## Problem Statement

CircuitBreakerStateManager swallows exceptions from the half-open resume callback and leaves the group stuck in HalfOpen/paused state. If a transport resume call throws, there is no retry or re-open path, so the group can stop consuming indefinitely until the process is restarted.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:272
- **Impact:** Resume failures are logged and discarded; consumers stay paused with no recovery path.

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

_To be determined during triage._

## Acceptance Criteria

- [ ] Resume failures either re-open the circuit or are retried
- [ ] Half-open state cannot permanently wedge a consumer group

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
