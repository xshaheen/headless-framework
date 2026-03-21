---
status: done
priority: p3
issue_id: "112"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Add dispose guard to RabbitMqConsumerClient.ResumeAsync

## Problem Statement

RabbitMqConsumerClient.ResumeAsync calls _channel!.BasicConsumeAsync(...) without checking if the client has been disposed. If ResumeAsync fires from the HalfOpen timer callback (via Task.Run) after DisposeAsync disposes _channel, it throws ObjectDisposedException inside Task.Run, which gets logged as an unhandled error. Low-probability race but produces noisy error logs on shutdown during open circuit window.

## Findings

- **Location:** src/Headless.Messaging.RabbitMq/RabbitMqConsumerClient.cs:135-143
- **Risk:** ObjectDisposedException logged as unhandled error on shutdown during open circuit
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer

## Proposed Solutions

### Add _disposed int field with Volatile.Read guard in ResumeAsync, matching transport TCS pattern
- **Pros**: Consistent with other transports
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add `private int _disposed` field. Check `if (Volatile.Read(ref _disposed) != 0) return;` at start of ResumeAsync. Set in DisposeAsync before channel disposal.

## Acceptance Criteria

- [ ] ResumeAsync no-ops after dispose
- [ ] No ObjectDisposedException logged on shutdown

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
