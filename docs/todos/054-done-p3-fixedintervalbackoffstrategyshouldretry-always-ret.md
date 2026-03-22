---
status: done
priority: p3
issue_id: "054"
tags: ["code-review","quality"]
dependencies: []
---

# FixedIntervalBackoffStrategy.ShouldRetry always returns true — should reject permanent exceptions

## Problem Statement

FixedIntervalBackoffStrategy.ShouldRetry always returns true (with comment 'maintains legacy behavior'). ExponentialBackoffStrategy.ShouldRetry correctly returns false for SubscriberNotFoundException (permanent, non-retryable). When FixedIntervalBackoffStrategy is active and a consumer throws SubscriberNotFoundException, messages will be retried FailedRetryCount times before dead-lettering — burning retry budget and storage I/O for a message that can never succeed.

## Findings

- **Location:** src/Headless.Messaging.Core/Retry/FixedIntervalBackoffStrategy.cs:27-30
- **Discovered by:** security-sentinel (P3)

## Proposed Solutions

### Return false for SubscriberNotFoundException (and other known permanent exceptions) in FixedIntervalBackoffStrategy
- **Pros**: Consistent with ExponentialBackoffStrategy, stops wasted retries
- **Cons**: Technically a behavior change for existing deployments using FixedInterval
- **Effort**: Small
- **Risk**: Low

### Extract shared permanent-exception check to SubscribeExecutor, applied before delegating to strategy
- **Pros**: DRY, strategies focus only on interval
- **Cons**: More refactoring
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Extract a shared `IsPermanentException` check and apply it before delegating to the strategy. Both strategies then only handle retry interval logic.

## Acceptance Criteria

- [x] SubscriberNotFoundException not retried regardless of strategy
- [x] Behavior consistent between FixedInterval and Exponential strategies
- [x] Tests verify permanent exception rejection in FixedInterval strategy

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

### 2026-03-22 - Resolved

**By:** Agent
**Actions:**
- Replicated ExponentialBackoffStrategy's permanent-exception pattern into FixedIntervalBackoffStrategy
- ShouldRetry now returns false for SubscriberNotFoundException, ArgumentNullException, ArgumentException, InvalidOperationException, NotSupportedException
- GetNextDelay now returns null when exception is permanent (matching ExponentialBackoffStrategy)
- Updated tests to verify permanent exception rejection
- Status changed: in-progress → done

### 2026-03-22 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
