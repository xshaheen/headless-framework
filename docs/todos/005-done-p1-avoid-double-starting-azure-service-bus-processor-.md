---
status: done
priority: p1
issue_id: "005"
tags: ["code-review","dotnet","architecture","quality"]
dependencies: []
---

# Avoid double-starting Azure Service Bus processor after paused startup

## Problem Statement

AzureServiceBusConsumerClient.ResumeAsync now calls StartProcessingAsync when a client was paused before startup, but ListeningAsync also calls StartProcessingAsync after waiting on the pause gate. In the paused-before-startup path, resuming the client can start the same processor twice, which can throw or leave the transport in an invalid state.

## Findings

- **Location:** src/Headless.Messaging.AzureServiceBus/AzureServiceBusConsumerClient.cs:122-177
- **Risk:** Late-starting Azure Service Bus consumers can fail resume/startup after a circuit-open pause
- **Discovered by:** rerun-review local verification

## Proposed Solutions

### Guard StartProcessingAsync with IsProcessing
- **Pros**: Minimal change, keeps current gate design
- **Cons**: Still spreads startup ownership across ListeningAsync and ResumeAsync
- **Effort**: Small
- **Risk**: Medium

### Make ListeningAsync the sole startup owner
- **Pros**: Cleaner lifecycle ownership, avoids duplicate start races
- **Cons**: Requires ResumeAsync to only open the gate before first start
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Use ListeningAsync as the only path that starts the processor before first run; ResumeAsync should just open the gate when startup has not yet begun, and only call StartProcessingAsync for already-started processors.

## Acceptance Criteria

- [ ] Paused-before-startup Azure Service Bus consumers resume without calling StartProcessingAsync twice
- [ ] ResumeAsync remains idempotent for already-started processors
- [ ] Unit tests cover pause-before-startup followed by resume

## Notes

Rerun review after resolving todos 001-004 for PR #194

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
