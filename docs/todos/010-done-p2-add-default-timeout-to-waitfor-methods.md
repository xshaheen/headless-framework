---
status: done
priority: p2
issue_id: "010"
tags: ["code-review","api-design"]
dependencies: []
---

# Add default timeout to WaitFor* methods

## Problem Statement

Every WaitFor* call requires explicit TimeSpan.FromSeconds(5). MassTransit.TestHarness defaults to 5s. Forcing every test to specify timeout is boilerplate tax.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessagingTestHarness.cs:140-193
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Make timeout nullable with 5s default: TimeSpan? timeout = null → timeout ?? TimeSpan.FromSeconds(5)

## Acceptance Criteria

- [ ] WaitForConsumed<T>() works without explicit timeout
- [ ] Explicit timeout still overrides default
- [ ] Default value documented in XML docs

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-22 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
