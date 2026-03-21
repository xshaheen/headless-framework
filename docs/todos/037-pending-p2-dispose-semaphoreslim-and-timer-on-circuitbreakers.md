---
status: pending
priority: p2
issue_id: "037"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Dispose SemaphoreSlim and Timer on CircuitBreakerStateManager shutdown

## Problem Statement

GroupCircuitState holds a SemaphoreSlim (ProbePermit — currently dead code but still allocated) and a Timer (OpenTimer). Neither is disposed when the manager shuts down. SemaphoreSlim implements IDisposable and allocates a ManualResetEventSlim when AvailableWaitHandle is accessed. OpenTimer is disposed during state transitions but not during process shutdown if a circuit is open at the time. CircuitBreakerStateManager does not implement IDisposable.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs - GroupCircuitState
- **Risk:** Low in practice (singleton), but violates IDisposable contract
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Implement IDisposable on CircuitBreakerStateManager
- **Pros**: Correct resource management
- **Cons**: Minor boilerplate
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Implement IDisposable on CircuitBreakerStateManager. In Dispose(), iterate all _groups values and dispose each state's ProbePermit SemaphoreSlim and OpenTimer. Register with DI lifecycle if possible.

## Acceptance Criteria

- [ ] CircuitBreakerStateManager implements IDisposable
- [ ] All GroupCircuitState OpenTimers disposed on shutdown
- [ ] All ProbePermit SemaphoreSlims disposed on shutdown

## Notes

Discovered during PR #194 code review (round 2). Note: ProbePermit may be removed per todo 019 - coordinate.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
