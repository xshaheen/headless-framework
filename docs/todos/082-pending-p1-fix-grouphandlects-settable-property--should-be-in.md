---
status: pending
priority: p1
issue_id: "082"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix GroupHandle.Cts settable property — should be init-only

## Problem Statement

GroupHandle.Cts is declared as `required CancellationTokenSource { get; set; }` — publicly settable after construction. There is no code path in this PR that reassigns it, but a mutable required property on a lifecycle-sensitive internal object is a bug waiting to happen. If any future code reassigns Cts after consumers are started, the old CTS-linked consumers would stop cancelling correctly.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs (GroupHandle class)
- **Risk:** Accidental reassignment of lifecycle-critical CancellationTokenSource
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Change { get; set; } to { get; init; }
- **Pros**: Prevents reassignment after initialization, consistent with required init pattern
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change `required CancellationTokenSource Cts { get; set; }` to `required CancellationTokenSource Cts { get; init; }`.

## Acceptance Criteria

- [ ] GroupHandle.Cts is init-only
- [ ] No runtime behavior change

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
