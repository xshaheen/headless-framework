---
status: pending
priority: p2
issue_id: "093"
tags: ["code-review","messaging","api-design"]
dependencies: []
---

# Add CancellationToken parameter to ICircuitBreakerStateManager.ReportFailureAsync and ReportSuccessAsync

## Problem Statement

ICircuitBreakerStateManager.ReportFailureAsync and ReportSuccessAsync are ValueTask-returning async methods that invoke pause/resume transport callbacks but accept no CancellationToken. If a transport's PauseAsync hangs, ReportFailureAsync hangs indefinitely with no cancellation escape. For a framework library, every public async method should propagate cancellation.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerStateManager.cs
- **Problem:** Async methods without CancellationToken cannot be cancelled
- **Impact:** Hung transport PauseAsync blocks message processing thread indefinitely
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Add CancellationToken with default value to both methods
- **Pros**: Correct async API contract for framework library
- **Cons**: API change — update all call sites
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add `CancellationToken cancellationToken = default` to both ICircuitBreakerStateManager.ReportFailureAsync and ReportSuccessAsync. Propagate to internal pause/resume callback invocations.

## Acceptance Criteria

- [ ] ReportFailureAsync accepts CancellationToken
- [ ] ReportSuccessAsync accepts CancellationToken
- [ ] CancellationToken propagated to pauseCallback/resumeCallback invocations
- [ ] All call sites updated

## Notes

PR #194 code review finding. This is a public API addition.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
