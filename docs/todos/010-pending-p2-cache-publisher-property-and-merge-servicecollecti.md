---
status: pending
priority: p2
issue_id: "010"
tags: ["code-review","quality"]
dependencies: []
---

# Cache Publisher property and merge ServiceCollectionExtensions class

## Problem Statement

Two issues: (1) harness.Publisher resolves from DI on every access instead of caching the singleton. (2) ServiceCollectionExtensions is a maximally generic class name; AddMessagingTestHarness belongs in the existing MessagingTestHarnessExtensions class.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessagingTestHarness.cs:221
- **Location:** src/Headless.Messaging.Testing/MessagingTestHarnessExtensions.cs:29
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Resolve and cache IMessagePublisher in constructor/factory. Move AddMessagingTestHarness extension into MessagingTestHarnessExtensions, delete ServiceCollectionExtensions class.

## Acceptance Criteria

- [ ] Publisher is resolved once and cached
- [ ] ServiceCollectionExtensions class removed
- [ ] AddMessagingTestHarness lives in MessagingTestHarnessExtensions
- [ ] All tests pass

## Notes

Source: Code review

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
