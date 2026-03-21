---
status: done
priority: p1
issue_id: "025"
tags: ["code-review","dotnet","data-integrity","performance"]
dependencies: []
---

# Keep retry lock TTL in sync with adaptive polling interval

## Problem Statement

MessageNeedToRetryProcessor computes its distributed lock TTL once from the base retry interval, but adaptive polling can later raise _currentInterval well above that fixed TTL. When UseStorageLock is enabled in a multi-instance deployment, the instance can sleep longer than the lock lease and another node can acquire the same received_retry lock, leading to duplicate retry processing while the first node still believes it owns the work.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:53
- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:84
- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:91
- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:158
- **Risk:** High - adaptive backoff can break the distributed lock contract and cause duplicate retry execution across instances

## Proposed Solutions

### Derive TTL from current interval
- **Pros**: Keeps lease duration aligned with actual sleep time
- **Cons**: Touches both acquire and renew paths
- **Effort**: Small
- **Risk**: Low

### Disable adaptive growth when storage locking is enabled
- **Pros**: Preserves existing lock assumptions
- **Cons**: Gives up backpressure in clustered deployments
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Base the lock TTL and renew duration on the effective current interval plus safety margin, and add a clustered regression test that proves the lease does not expire before the next renew.

## Acceptance Criteria

- [ ] received_retry and publish_retry lock TTLs stay greater than the effective wait interval used by the processor
- [ ] RenewLockAsync uses the same effective TTL contract as AcquireLockAsync
- [ ] A regression test covers adaptive polling with UseStorageLock enabled

## Notes

Discovered during PR #194 review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: pending → done
