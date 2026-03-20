---
status: pending
priority: p2
issue_id: "011"
tags: ["code-review","quality"]
dependencies: []
---

# Fix _EnsureInMemoryInfrastructure misleading XML doc and dead code

## Problem Statement

XML doc says 'If the markers are missing, this is a no-op — the bootstrapper will surface a clear error' but the code actually throws InvalidOperationException immediately. Also, the outer if(!hasQueueMarker || !hasStorageMarker) wrapping the inner individual checks is redundant — the second if is unreachable when the first throws.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessagingTestHarness.cs:270-308
- **Discovered by:** pragmatic-dotnet-reviewer, strict-dotnet-reviewer, code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Fix the XML doc to match the throwing behavior. Remove the outer if guard — just two sequential if-throw statements.

## Acceptance Criteria

- [ ] XML doc accurately describes the throwing behavior
- [ ] Outer if guard removed — two flat if-throw statements
- [ ] All tests pass

## Notes

Source: Code review

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
