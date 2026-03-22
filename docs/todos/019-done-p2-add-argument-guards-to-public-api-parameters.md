---
status: done
priority: p2
issue_id: "019"
tags: ["code-review","conventions"]
dependencies: []
---

# Add Argument.* guards to public API parameters

## Problem Statement

Per project conventions, public APIs must use Headless.Checks guards. CreateAsync(configure) and WaitForAsync(messageType) lack null guards.

## Findings

- **CreateAsync:** src/Headless.Messaging.Testing/MessagingTestHarness.cs:61
- **WaitForAsync:** src/Headless.Messaging.Testing/MessageObservationStore.cs:66
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add Argument.IsNotNull(configure) and Argument.IsNotNull(messageType).

## Acceptance Criteria

- [ ] All public method parameters validated with Argument.* guards
- [ ] ArgumentException thrown for null inputs

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
