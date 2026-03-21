---
status: done
priority: p3
issue_id: "102"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Add stress test for concurrent Dispose and timer callback race in CircuitBreakerStateManager

## Problem Statement

The test suite covers most circuit breaker state transitions but lacks a stress test for the concurrent Dispose + timer-callback race (Pattern 5 in docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md). This is the most dangerous timing window in the state manager.

## Findings

- **Location:** tests/Headless.Messaging.Core.Tests.Unit/CircuitBreaker/CircuitBreakerStateManagerTests.cs
- **Risk:** Untested concurrency scenario — Dispose during HalfOpen transition
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Add 200-iteration stress test: create manager, trip circuit, simultaneously Dispose from two threads
- **Pros**: Catches Dispose+timer race, validates _disposed guard
- **Cons**: Slightly slower test suite
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add: loop 200x creating a manager with failureThreshold=1 and openDuration=1ms, ReportFailureAsync to trip, then Task.WhenAll(Task.Run(Dispose), Task.Run(Dispose)). No exception = pass.

## Acceptance Criteria

- [ ] Stress test added and passes reliably
- [ ] No exceptions thrown during concurrent Dispose

## Notes

PR #194.

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
