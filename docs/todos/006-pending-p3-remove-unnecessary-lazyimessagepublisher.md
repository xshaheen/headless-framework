---
status: pending
priority: p3
issue_id: "006"
tags: ["code-review","simplicity"]
dependencies: []
---

# Remove unnecessary Lazy<IMessagePublisher>

## Problem Statement

Lazy<T> wraps a GetRequiredService call on a built singleton container — just a dictionary lookup. The Lazy adds ceremony with no benefit.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessagingTestHarness.cs:39,46,210
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Replace with ServiceProvider.GetRequiredService<IMessagePublisher>() directly in the property getter.

## Acceptance Criteria

- [ ] Lazy<T> removed
- [ ] Publisher property resolves directly from ServiceProvider

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
