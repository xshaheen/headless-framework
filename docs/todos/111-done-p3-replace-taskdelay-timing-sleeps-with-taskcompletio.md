---
status: done
priority: p3
issue_id: "111"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Replace Task.Delay timing sleeps with TaskCompletionSource callbacks in circuit breaker tests

## Problem Statement

14 tests in CircuitBreakerStateManagerTests.cs use await Task.Delay(150) (with openDuration: 30ms) to wait for timer callbacks. This is a 5x margin but will flake on slow CI runners (macOS) where ThreadPool backpressure can delay both the timer and the subsequent Task.Run well past 150ms. The tests are not marked as timing-sensitive.

## Findings

- **Location:** tests/Headless.Messaging.Core.Tests.Unit/CircuitBreaker/CircuitBreakerStateManagerTests.cs:143,156,175,199,226,251,282,286,292,294,316,352,357,362,394,399,447,509,527,593
- **Risk:** Latent flakes on slow CI hardware
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer, compound-engineering:review:code-simplicity-reviewer

## Proposed Solutions

### Use TaskCompletionSource set inside onResume/onPause callbacks, await with WaitAsync(TimeSpan.FromSeconds(5))
- **Pros**: Deterministic — resolves immediately when callback fires; no magic numbers
- **Cons**: Test refactor
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Use TCS-based pattern: register onResume callback that calls tcs.TrySetResult(); await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)) instead of Task.Delay(150).

## Acceptance Criteria

- [ ] No Task.Delay calls in timer-dependent tests
- [ ] Tests resolve immediately when timer fires
- [ ] 5s WaitAsync timeout as flake safety net

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
