---
status: done
priority: p3
issue_id: "014"
tags: ["code-review","api-design"]
dependencies: []
---

# Consider adding count-based WaitFor* overloads

## Problem Statement

For fan-out scenarios (one publish triggers N consumers), there is no WaitForConsumed<T>(count, timeout). Users must compose multiple awaits manually or use Task.WhenAll.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessagingTestHarness.cs:140-193
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add WaitForConsumed<T>(int count, TimeSpan timeout) returning IReadOnlyList<RecordedMessage>. Can be deferred to a future iteration.

## Acceptance Criteria

- [ ] Count-based overloads available for Published/Consumed/Faulted

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
