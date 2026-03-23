---
status: done
priority: p1
issue_id: "084"
tags: ["code-review","messaging","rabbitmq","correctness"]
dependencies: []
---

# Fix RabbitMqConsumerClient.ResumeAsync null-forgiving _channel! NullRefException after disposal

## Problem Statement

RabbitMqConsumerClient.ResumeAsync calls _channel!.BasicConsumeAsync(...) using the null-forgiving operator. If the client has been disposed (and _channel set to null), this throws NullReferenceException. The same pattern exists for BasicCancelAsync in PauseAsync. ConsumerPauseGate.Release() unblocks the gate but the channel operation happens after — the race window is real.

## Findings

- **Location:** src/Headless.Messaging.RabbitMq/RabbitMqConsumerClient.cs:~131
- **Problem:** _channel! null-forgiving used on potentially-null channel after disposal
- **Same pattern:** PauseAsync also uses _channel!.BasicCancelAsync(...)
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Add disposal guard at top of PauseAsync/ResumeAsync
- **Pros**: Consistent with other IConsumerClient implementations, prevents NRE
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add ObjectDisposedException guard at the start of both PauseAsync and ResumeAsync. Check _disposed flag (or _channel == null) before attempting channel operations.

## Acceptance Criteria

- [ ] PauseAsync returns ValueTask.CompletedTask (or throws ObjectDisposedException) if disposed
- [ ] ResumeAsync returns ValueTask.CompletedTask (or throws ObjectDisposedException) if disposed
- [ ] No NullReferenceException when calling pause/resume after disposal
- [ ] Unit test: PauseAsync/ResumeAsync on disposed client

## Notes

PR #194 code review finding. Also verify AmazonSqsConsumerClient and KafkaConsumerClient _disposed guard pattern — both have dead _disposed fields that are set but never read in guard paths.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-23 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
