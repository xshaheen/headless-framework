---
status: pending
priority: p2
issue_id: "062"
tags: ["code-review","performance"]
dependencies: []
---

# OTel metrics recorded inside group lock — inflates lock hold time

## Problem Statement

metrics.RecordTrip and metrics.RecordOpenDuration called inside lock(_lock) in CircuitBreakerStateManager transitions. OTel exporters may perform I/O — inflates lock hold time and creates contention under load.

## Findings

- **Location:** CircuitBreakerStateManager.cs (transitions)
- **Risk:** Medium — lock contention under OTel exporters
- **Discovered by:** learnings-researcher, performance-oracle

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Capture metric parameters as locals inside lock, call metrics outside after releasing lock. Same pattern already used for callbacks.

## Acceptance Criteria

- [ ] RecordTrip called outside group lock
- [ ] RecordOpenDuration called outside group lock
- [ ] Metric parameters captured as locals inside lock

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
