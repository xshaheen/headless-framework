---
status: done
priority: p2
issue_id: "027"
tags: ["code-review","observability"]
dependencies: []
---

# Fix GetAllStates() returning empty before RegisterKnownGroups — OTel gauge gap

## Problem Statement

GetAllStates() (CircuitBreakerStateManager.cs:321-324) returns data from _groups which is populated lazily. OTel observable gauge scrape during startup (before ConsumerRegister.ExecuteAsync calls RegisterKnownGroups) gets an empty list, causing a silent gap in metrics. Grafana shows zero data then suddenly all groups appear.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:321-324
- **Risk:** Medium — alert gaps during startup window
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Document the ordering dependency or pre-seed the gauge with known group names from a separate collection so the gauge always has the right shape.

## Acceptance Criteria

- [ ] OTel gauge emits all known groups even before first message
- [ ] No silent gap during startup window

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
