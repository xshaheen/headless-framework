---
status: pending
priority: p2
issue_id: "032"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Register circuit breaker callbacks before starting consumer tasks

## Problem Statement

In ConsumerRegister.ExecuteAsync, consumer tasks are started in the loop (handle.ConsumerTasks.Add(task)) and then RegisterGroupCallbacks is called AFTER. There is a window where the tasks have started, call SubscribeExecutor which calls ReportFailureAsync, and if a failure occurs immediately, the circuit tries to invoke state.OnPause — but OnPause is null because RegisterGroupCallbacks has not been called yet. The early failure silently misses the pause call.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:187-241
- **Risk:** Medium - early failures can open circuit without pausing consumers
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Call RegisterGroupCallbacks before starting consumer tasks
- **Pros**: No race window
- **Cons**: Callbacks registered before handle is populated with clients
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Move the _circuitBreakerStateManager?.RegisterGroupCallbacks(...) call to before the consumer task start loop. The callbacks only need the handle reference (captured by closure), not the populated Clients list.

## Acceptance Criteria

- [ ] RegisterGroupCallbacks called before first consumer task starts
- [ ] OnPause/OnResume not null when first failure is reported

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
