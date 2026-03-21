---
status: done
priority: p2
issue_id: "019"
tags: ["code-review","concurrency","cleanup"]
dependencies: []
---

# Recreate linked shutdown token on consumer restart

## Problem Statement

ConsumerRegister.ReStartAsync replaces _stoppingCts with a new standalone CancellationTokenSource after PulseAsync disposes the linked token created in StartAsync. Restarted consumer loops no longer observe the host shutdown token, so a later service stop can leave restarted consumers running and holding resources.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:79
- **Risk:** Restarted consumers outlive the host shutdown token and can leak work during shutdown

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

_To be determined during triage._

## Acceptance Criteria

- [ ] Restarted consumer CTS remains linked to the original stopping token
- [ ] Host shutdown cancels both initial and restarted consumer loops
- [ ] Regression test covers ReStartAsync after StartAsync

## Notes

PR #194 review finding

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: pending → done
