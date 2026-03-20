---
status: done
priority: p2
issue_id: "007"
tags: ["code-review","messaging","circuit-breaker","dotnet","performance"]
dependencies: []
---

# State field in GroupCircuitState not volatile — IsOpen lockless read unsafe on ARM64

## Problem Statement

CircuitBreakerStateManager.IsOpen reads state.State (a CircuitBreakerState enum backed by int) without a lock, relying on the comment that int reads are atomic on .NET. Atomicity is correct, but without volatile or Volatile.Read, .NET's memory model does not guarantee that the reading thread sees the latest write on weakly-ordered architectures. ARM64 (.NET 10 deployments) has a weaker memory model than x86/x64 — a stale Closed value could be observed from a different thread without a memory barrier. IsOpen is on the hot path (called per-message in retry processor).

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:IsOpen + GroupCircuitState.State
- **Risk:** Stale circuit state read on ARM64 — circuit appears closed when open
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Mark State field as volatile
- **Pros**: Simplest, zero overhead
- **Cons**: volatile on enum requires backing field pattern
- **Effort**: Small
- **Risk**: Low

### Use Volatile.Read in IsOpen
- **Pros**: Explicit at call site
- **Cons**: Slightly verbose
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Use Volatile.Read in IsOpen: return Volatile.Read(ref state._stateBacking) is CircuitBreakerState.Open or CircuitBreakerState.HalfOpen where _stateBacking is an int field. Or mark State as volatile.

## Acceptance Criteria

- [ ] IsOpen is safe on ARM64 without a lock
- [ ] All writes to State also use Volatile.Write or happen inside the lock (which already provides barrier)

## Notes

PR #194 review.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-20 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
