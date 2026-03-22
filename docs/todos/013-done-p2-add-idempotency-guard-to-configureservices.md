---
status: done
priority: p2
issue_id: "013"
tags: ["code-review","correctness"]
dependencies: []
---

# Add idempotency guard to ConfigureServices

## Problem Statement

Calling ConfigureServices twice (e.g. two AddMessagingTestHarness() calls) registers a second MessageObservationStore and wraps RecordingTransport in another RecordingTransport, double-recording every message.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessagingTestHarness.cs:96-117
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add a marker service check (like _EnsureInMemoryInfrastructure pattern) — if marker present, return early.

## Acceptance Criteria

- [ ] Calling AddMessagingTestHarness() twice is a no-op
- [ ] Messages recorded exactly once

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
