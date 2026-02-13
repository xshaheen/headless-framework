---
status: pending
priority: p2
issue_id: "007"
tags: ["code-review","dotnet","quality","scheduling"]
dependencies: []
---

# Fix generic scheduled string payload deserialization mismatch

## Problem Statement

ScheduleOnceAsync<TConsumer, TPayload> serializes payload with JsonSerializer, but ScheduledJobConsumer<string> bypasses JSON deserialization and returns raw payload text. For string payloads this yields quoted JSON text (e.g., "hello" instead of hello), causing runtime behavior mismatch.

## Findings

- **Serialization point:** src/Headless.Messaging.Core/Scheduling/ScheduledJobManager.cs:197
- **String bypass:** src/Headless.Messaging.Abstractions/ScheduledJobConsumer.cs:120

## Proposed Solutions

### Always deserialize payload JSON in ScheduledJobConsumer<TPayload>
- **Pros**: Consistent semantics for all payload types
- **Cons**: Raw non-JSON payloads for string must use non-generic API intentionally
- **Effort**: Small
- **Risk**: Low

### Special-case string in ScheduleOnceAsync<TConsumer,TPayload>
- **Pros**: Preserves existing raw-string consumer behavior
- **Cons**: Splits payload semantics by call path and is harder to reason about
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Use consistent JSON semantics for generic payload scheduling and add explicit tests for string payload round-trip.

## Acceptance Criteria

- [ ] ScheduleOnceAsync<TConsumer,string>(..., "hello") results in Payload == "hello" in ScheduledJobConsumer<string>
- [ ] Existing object payload deserialization tests continue to pass
- [ ] A regression test covers both generic and raw-string scheduling paths

## Notes

Discovered during PR #177 review.

## Work Log

### 2026-02-13 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
