---
status: done
priority: p2
issue_id: "035"
tags: ["code-review","error-handling"]
dependencies: []
---

# Fix fire-and-forget tasks swallowing exceptions in retry processor

## Problem Statement

_ProcessPublishedAsync and _ProcessReceivedAsync are started as fire-and-forget tasks (IProcessor.NeedRetry.cs:96-103,119-126). If they throw beyond what _GetSafelyAsync catches (e.g., lock acquire/release failures), exceptions are silently swallowed. The ContinueWith on _failedRetryConsumeTask only nulls the reference — it does not observe or log exceptions.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:96-103,119-126
- **Discovered by:** strict-dotnet-reviewer
- **Known Pattern:** docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md — Pattern 6

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Attach .ContinueWith(t => logger.LogError(t.Exception, ...), TaskContinuationOptions.OnlyOnFaulted) to both tasks.

## Acceptance Criteria

- [ ] Both fire-and-forget tasks have exception observation via ContinueWith
- [ ] Exceptions logged with appropriate severity
- [ ] No silent task failures

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

### 2026-03-22 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
