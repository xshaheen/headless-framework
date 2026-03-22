---
status: pending
priority: p2
issue_id: "037"
tags: ["code-review","threading"]
dependencies: []
---

# Fix _disposed reset in ConsumerRegister.StartAsync defeating idempotency guard

## Problem Statement

ConsumerRegister.StartAsync (IConsumerRegister.cs:75) resets _disposed to 0 after ExecuteAsync. This defeats the CompareExchange(ref _disposed, 1, 0) guard in DisposeAsync — two callers can both see _disposed=0 and both enter DisposeAsync.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:75
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Use a proper state machine (NotStarted/Running/Disposing/Disposed) instead of a boolean _disposed flag if restartability is needed. Or remove the reset if not restartable.

## Acceptance Criteria

- [ ] Dispose guard cannot be bypassed by StartAsync reset
- [ ] Restartability semantics clearly documented

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
