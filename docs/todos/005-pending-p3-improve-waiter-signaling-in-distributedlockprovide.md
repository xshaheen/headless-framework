---
status: pending
priority: p3
issue_id: "005"
tags: []
dependencies: []
---

# Improve waiter signaling in DistributedLockProvider

## Problem Statement

AsyncAutoResetEvent.Set() only wakes one waiter; if that waiter is timed out, others may starve until their backoff timer fires.

## Findings

- **Status:** Identified during workflow execution
- **Priority:** p3

## Proposed Solutions

_To be analyzed during triage or investigation._

## Recommended Action

_To be determined during triage._

## Acceptance Criteria
- [ ] Signaling logic ensures next active waiter is woken
- [ ] Lock acquisition latency remains stable under extreme contention

## Notes

Source: Workflow automation

## Work Log

### 2026-04-16 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create
