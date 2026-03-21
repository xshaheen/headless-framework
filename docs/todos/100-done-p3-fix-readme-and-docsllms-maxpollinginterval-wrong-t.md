---
status: done
priority: p3
issue_id: "100"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix README and docs/llms MaxPollingInterval wrong type (int vs TimeSpan)

## Problem Statement

src/Headless.Messaging.Core/README.md and docs/llms/messaging.txt both show MaxPollingInterval = 900 (an integer representing seconds). The property type is TimeSpan. This will not compile and immediately confuses framework consumers.

## Findings

- **Location:** src/Headless.Messaging.Core/README.md (~line 4257), docs/llms/messaging.txt (~line 99)
- **Risk:** Documentation error — will not compile; confuses adopters immediately
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, code-simplicity-reviewer

## Proposed Solutions

### Change to TimeSpan.FromMinutes(15) in both files
- **Pros**: Correct and readable
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace `MaxPollingInterval = 900` with `MaxPollingInterval = TimeSpan.FromMinutes(15)` in README.md and docs/llms/messaging.txt.

## Acceptance Criteria

- [ ] README.md shows correct TimeSpan usage
- [ ] docs/llms/messaging.txt shows correct TimeSpan usage
- [ ] Example compiles

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
