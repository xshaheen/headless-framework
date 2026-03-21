---
status: pending
priority: p1
issue_id: "031"
tags: ["code-review","dotnet","performance"]
dependencies: []
---

# Move OTel metrics calls outside group lock in CircuitBreakerStateManager

## Problem Statement

metrics.RecordTrip() and metrics.RecordOpenDuration() are called inside the group lock in _TransitionToOpen and _TransitionToClosed. OTel Counter.Add and Histogram.Record can briefly acquire internal meter-provider locks. Doing this under the group lock creates an unnecessary nested-lock sequence that inflates lock hold time. Under high-frequency trip/close cycles (flapping dependency), this delays competing threads calling ReportFailureAsync/ReportSuccess on the same group.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:193 - metrics.RecordTrip inside lock
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:_TransitionToClosed - metrics.RecordOpenDuration inside lock
- **Risk:** High - lock hold time inflation on hot path
- **Discovered by:** compound-engineering:review:performance-oracle

## Proposed Solutions

### Extract metrics calls outside the lock
- **Pros**: Eliminates nested lock risk, reduces lock hold time
- **Cons**: Slightly more verbose
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Capture groupName/duration values inside the lock, then call metrics methods after the lock block releases — same pattern as pauseCallback/resumeCallback already used in the file.

## Acceptance Criteria

- [ ] metrics.RecordTrip not called while holding groupLock
- [ ] metrics.RecordOpenDuration not called while holding groupLock
- [ ] Existing tests still pass

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
