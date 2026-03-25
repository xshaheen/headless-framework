---
status: pending
priority: p2
issue_id: "124"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Restore in-memory received-exception persistence for plain string content

## Problem Statement

InMemoryDataStorage.StoreReceivedExceptionMessageAsync now deserializes the supplied content and throws if it is not a serialized Message. The method signature still accepts an arbitrary string, and the current in-memory unit test that passes raw exception text now fails.

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

Restore plain-string tolerance in the in-memory implementation unless the team is ready to tighten the contract across all providers and callers deliberately.

## Acceptance Criteria

- [ ] StoreReceivedExceptionMessageAsync no longer throws for the existing in-memory raw-string test case
- [ ] The intended contract for exception-message content is explicit and consistent across providers
- [ ] The focused in-memory unit test for should_store_received_exception_message passes

## Notes

Confirmed by focused test run on 2026-03-25: dotnet test --project tests/Headless.Messaging.InMemoryStorage.Tests.Unit/Headless.Messaging.InMemoryStorage.Tests.Unit.csproj --no-restore -- --filter-method '*should_store_received_exception_message'

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
