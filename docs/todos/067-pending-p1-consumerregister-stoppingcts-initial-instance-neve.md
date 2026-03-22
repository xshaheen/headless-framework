---
status: pending
priority: p1
issue_id: "067"
tags: ["code-review","concurrency"]
dependencies: []
---

# ConsumerRegister _stoppingCts initial instance never disposed if DisposeAsync called before StartAsync

## Problem Statement

ConsumerRegister initializes `_stoppingCts = new CancellationTokenSource()` at field declaration (line 51). PulseAsync (called from DisposeAsync at line 138) disposes _stoppingCts and recreates it. However, PulseAsync was designed to clean up the PREVIOUSLY set linked CTS. If DisposeAsync is called before StartAsync was ever called, the bare `new CancellationTokenSource()` on line 51 is disposed by PulseAsync — so this path is actually covered. But if ReStartAsync (line 83) creates a new CTS and then ExecuteAsync throws, the newly created _stoppingCts at line 84 is never cleaned up — PulseAsync has already run (line 82) and will not run again for this CTS.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:78-92
- **Scenario:** ReStartAsync: PulseAsync disposes old CTS, new CTS created at line 84, ExecuteAsync throws, new CTS is leaked
- **Discovered by:** strict-dotnet-reviewer (P1.4, P2.5)

## Proposed Solutions

### Wrap new CTS creation in try/finally in ReStartAsync
- **Pros**: Minimal change, disposes on ExecuteAsync throw
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Wrap lines 83-92 of ReStartAsync in a try/catch that disposes _stoppingCts on ExecuteAsync failure, then rethrows.

## Acceptance Criteria

- [ ] CancellationTokenSource not leaked when ExecuteAsync throws in ReStartAsync
- [ ] Test: ReStartAsync with failing ExecuteAsync disposes the CTS created during restart

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
