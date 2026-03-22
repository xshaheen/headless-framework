---
status: done
priority: p3
issue_id: "012"
tags: ["code-review","api-design"]
dependencies: []
---

# Add Count/IsEmpty to TestConsumer<T> and doc linking to WaitFor

## Problem Statement

TestConsumer lacks Count and IsEmpty properties (ConcurrentQueue exposes them without allocation). Also no XML doc explaining relationship to WaitForConsumed.

## Findings

- **Location:** src/Headless.Messaging.Testing/TestConsumer.cs
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, agent-native-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add Count/IsEmpty properties and XML doc note: 'Use harness.WaitForConsumed<T>() for awaitable assertions; use ReceivedContexts for full ConsumeContext<T>.'

## Acceptance Criteria

- [ ] Count and IsEmpty properties added
- [ ] XML doc explains WaitFor vs ReceivedContexts relationship

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
