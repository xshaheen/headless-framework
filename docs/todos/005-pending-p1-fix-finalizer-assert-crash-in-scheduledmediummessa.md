---
status: pending
priority: p1
issue_id: "005"
tags: ["code-review","dotnet","quality","reliability"]
dependencies: []
---

# Fix finalizer assert crash in ScheduledMediumMessageQueue

## Problem Statement

The finalizer in ScheduledMediumMessageQueue calls Debug.Fail when Dispose() was not called, which crashes xUnit test execution and aborts the process. PR #177 still carries this behavior, and the full Headless.Messaging.Core.Tests.Unit run exits with code 134 before completion.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/ScheduledMediumMessageQueue.cs:113
- **Crash point:** src/Headless.Messaging.Core/Internal/ScheduledMediumMessageQueue.cs:118
- **Evidence:** dotnet test --project tests/Headless.Messaging.Core.Tests.Unit/Headless.Messaging.Core.Tests.Unit.csproj fails with Xunit.Sdk.TraceAssertException

## Proposed Solutions

### Finalizer should not assert
- **Pros**: Prevents process-level crashes; aligns with .NET dispose/finalizer guidance
- **Cons**: Silent leaks could be missed unless additional telemetry/logging is added
- **Effort**: Small
- **Risk**: Low

### Keep assert and enforce deterministic disposal everywhere
- **Pros**: Catches leaks aggressively
- **Cons**: Non-deterministic finalization still makes tests brittle; hard to guarantee everywhere
- **Effort**: Medium
- **Risk**: High


## Recommended Action

Use the non-asserting finalizer path and ensure Dispose() still releases managed resources when called.

## Acceptance Criteria

- [ ] Full tests/Headless.Messaging.Core.Tests.Unit suite runs without process crash
- [ ] ScheduledMediumMessageQueue finalizer no longer calls Debug.Fail
- [ ] A regression test covers disposal/finalizer behavior

## Notes

Discovered during PR #177 review.

## Work Log

### 2026-02-13 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
