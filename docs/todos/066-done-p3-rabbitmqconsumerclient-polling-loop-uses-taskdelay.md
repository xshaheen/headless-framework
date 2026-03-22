---
status: done
priority: p3
issue_id: "066"
tags: ["code-review","performance"]
dependencies: []
---

# RabbitMqConsumerClient polling loop uses Task.Delay(timeout) — should use Task.Delay(Infinite, ct)

## Problem Statement

RabbitMqConsumerClient.ListeningAsync uses 'await Task.Delay(timeout, cancellationToken)' in a tight polling loop (lines 111-114). RabbitMQ is push-based — BasicConsumeAsync registers a callback and returns immediately; the loop only exists to keep the task alive. Each Task.Delay allocates a TimerQueueTimer and a Task, causing ~1 allocation pair per second per consumer thread. Task.Delay(Timeout.Infinite, ct) achieves the same effect with zero periodic allocations.

## Findings

- **Location:** src/Headless.Messaging.RabbitMq/RabbitMqConsumerClient.cs:111-114
- **Discovered by:** performance-oracle (P3)

## Proposed Solutions

### Replace the while loop with await Task.Delay(Timeout.Infinite, cancellationToken)
- **Pros**: Single allocation, no periodic timer churn
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace 'while (!ct.IsCancellationRequested) { await Task.Delay(timeout, ct); }' with a single 'await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);' inside a try/catch for OperationCanceledException.

## Acceptance Criteria

- [ ] No periodic Task.Delay allocation in RabbitMQ ListeningAsync
- [ ] Cancellation still terminates the wait
- [ ] RabbitMQ unit tests still pass

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
