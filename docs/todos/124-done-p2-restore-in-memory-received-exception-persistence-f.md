---
status: done
priority: p2
issue_id: "124"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Make received-exception persistence content contract explicit across providers

## Problem Statement

StoreReceivedExceptionMessageAsync requires serialized Message content so providers can persist the logical MessageId and headers for failed received messages. The in-memory tests and harness were still treating the input as arbitrary raw text, which no longer matched the durable providers or the core caller path.

## Findings

- **Location:** src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs:185
- **Location:** tests/Headless.Messaging.InMemoryStorage.Tests.Unit/InMemoryDataStorageTests.cs:83
- **Risk:** High - a previously successful error-persistence path now throws InvalidOperationException
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer

## Proposed Solutions

### Keep plain-string support in the in-memory store
- **Pros**: Restores existing behavior and fixes the failing unit test
- **Cons**: MessageId may need to stay optional for in-memory failed rows
- **Effort**: Small
- **Risk**: Low

### Make serialized Message content the explicit contract everywhere
- **Pros**: Aligns all providers on one input shape
- **Cons**: Requires public contract clarification and broader caller/test updates
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Make serialized Message content the explicit contract across providers and align the in-memory tests and harness with that behavior.

## Acceptance Criteria

- [x] The shared storage contract makes serialized Message content explicit for received-exception persistence
- [x] In-memory tests and shared storage harness use serialized Message content consistently
- [x] The in-memory storage unit test suite passes with the aligned contract

## Notes

Confirmed by focused test run on 2026-03-25: dotnet test --project tests/Headless.Messaging.InMemoryStorage.Tests.Unit/Headless.Messaging.InMemoryStorage.Tests.Unit.csproj --no-restore -- --filter-method '*should_store_received_exception_message'

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-25 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-25 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
