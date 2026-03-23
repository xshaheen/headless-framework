---
status: ready
priority: p2
issue_id: "105"
tags: ["code-review","dotnet","thread-safety","disposal"]
dependencies: []
---

# RemoveGroup sync timer dispose + Dispose() ResumeTask gap

## Problem Statement

RemoveGroup calls synchronous Timer.Dispose() which doesn't guarantee in-flight callbacks completed. It also doesn't cancel _disposalCts so the timer callback guard doesn't apply. Separately, synchronous Dispose() doesn't await ResumeTask before disposing _disposalCts, risking ObjectDisposedException.

## Findings

- **RemoveGroup:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:303-319
- **Dispose():** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:596-619
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer, compound-engineering:review:security-sentinel

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Make RemoveGroup async and await timer DisposeAsync. In sync Dispose(), block on ResumeTask before disposing _disposalCts, or delegate to DisposeAsync().AsTask().GetAwaiter().GetResult().

## Acceptance Criteria

- [ ] RemoveGroup awaits timer disposal or documents accepted race
- [ ] Dispose() handles in-flight ResumeTask before CTS disposal

## Notes

DisposeAsync already handles this correctly — sync Dispose is the gap.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
