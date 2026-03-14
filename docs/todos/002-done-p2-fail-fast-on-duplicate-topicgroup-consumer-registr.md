---
status: done
priority: p2
issue_id: "002"
tags: ["code-review","architecture","dotnet","quality"]
dependencies: []
---

# Fail fast on duplicate topic/group consumer registrations

## Problem Statement

ConsumerRegistry now allows multiple class registrations for the same topic and group as long as the handler id differs, but ConsumerServiceSelector later deduplicates candidates only by topic and group. The extra registration is silently dropped during candidate selection instead of being rejected at configuration time, so the new deterministic duplicate policy is inconsistent and one handler can disappear without an error.

## Findings

- **Location:** src/Headless.Messaging.Core/ConsumerRegistry.cs:47-59
- **Location:** src/Headless.Messaging.Core/Internal/IConsumerServiceSelector.cs:62-75
- **Location:** src/Headless.Messaging.Core/Internal/ConsumerExecutorDescriptor.cs:25-38
- **Risk:** Medium - same topic/group registrations can be accepted but one is discarded later
- **Discovered by:** strict-dotnet-reviewer / code-simplicity-reviewer

## Proposed Solutions

### Reject same topic/group registrations in ConsumerRegistry
- **Pros**: Matches fail-fast contract and keeps runtime simple
- **Cons**: Breaking for any implicit competing-consumer usage
- **Effort**: Small
- **Risk**: Low

### Include handler identity in selector deduplication
- **Pros**: Preserves multiple registrations if they are truly supported
- **Cons**: May create duplicate broker registrations unless the rest of the pipeline is updated too
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Reject duplicate topic/group registrations at configuration time unless the runtime is intentionally updated to support them end-to-end.

## Acceptance Criteria

- [x] The duplicate policy is enforced consistently between registration and candidate selection
- [x] Same topic/group registrations either fail fast or remain visible all the way through execution
- [x] A regression test covers two different handlers registered with the same topic and group

## Notes

Discovered during PR #184 review

## Work Log

### 2026-03-11 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-12 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-12 - Fixed

**By:** Codex
**Actions:**
- Tightened `ConsumerRegistry` duplicate validation to reject same `topic/group` registrations regardless of handler id
- Applied the same duplicate check during metadata updates so builder reconfiguration cannot create hidden collisions
- Added registry- and builder-level regression tests for duplicate `topic/group` handlers

### 2026-03-12 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
