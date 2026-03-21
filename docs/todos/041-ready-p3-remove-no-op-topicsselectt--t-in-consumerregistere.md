---
status: ready
priority: p3
issue_id: "041"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Remove no-op topics.Select(t => t) in ConsumerRegister.ExecuteAsync

## Problem Statement

In IConsumerRegister.cs, line 189: var topicIds = topics.Select(t => t); This is a pure identity projection that creates an unnecessary SelectEnumerableIterator wrapper for zero benefit. The variable is used directly in the inner task closure.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:189
- **Discovered by:** compound-engineering:review:performance-oracle, code-simplicity-reviewer

## Proposed Solutions

### Remove the Select call
- **Pros**: Zero-overhead
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace with IEnumerable<string> topicIds = topics; or pass topics directly.

## Acceptance Criteria

- [ ] topics.Select(t => t) removed
- [ ] topics passed directly or via simple assignment

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
