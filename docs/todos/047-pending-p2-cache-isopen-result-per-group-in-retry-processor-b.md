---
status: pending
priority: p2
issue_id: "047"
tags: ["code-review","performance"]
dependencies: []
---

# Cache IsOpen() result per group in retry processor batch loop

## Problem Statement

_ProcessReceivedAsync (IProcessor.NeedRetry.cs:202-203) calls message.Origin.GetGroup() (header dict lookup) + IsOpen(group) (ConcurrentDictionary lookup + Volatile.Read) for every retry message. At 1000 messages/batch, this is 2000 dictionary lookups per invocation.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:202-203
- **Discovered by:** performance-oracle

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Cache IsOpen() result per unique group name using a Dictionary<string, bool>. Reduces lookups from O(N) per message to O(G) per unique group.

## Acceptance Criteria

- [ ] IsOpen() called once per unique group per batch, not once per message
- [ ] Cache invalidated between batches

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
