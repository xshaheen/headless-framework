---
status: pending
priority: p1
issue_id: "007"
tags: ["code-review","quality"]
dependencies: []
---

# Deduplicate CreateAsync and ConfigureServices setup logic

## Problem Statement

MessagingTestHarness.CreateAsync (lines 64-103) and ConfigureServices (lines 117-138) contain 8 identical setup steps: _EnsureInMemoryInfrastructure, Configure<MessagingOptions>, AddSingleton(store), _DecorateTransport, _DecoratePipeline, TryAddSingleton<IMessagePublisher>. A change to one path requires a matching change to the other — maintenance hazard.

## Findings

- **Location:** src/Headless.Messaging.Testing/MessagingTestHarness.cs:64-103 vs 117-138
- **Discovered by:** code-simplicity-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### CreateAsync delegates to ConfigureServices
- **Pros**: Single source of truth, ConfigureServices stays internal
- **Cons**: Minor refactor
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Make CreateAsync call ConfigureServices internally, only adding AddLogging() and BuildServiceProvider/Bootstrap on top.

## Acceptance Criteria

- [ ] CreateAsync delegates to ConfigureServices for shared setup
- [ ] No duplicated setup steps between the two paths
- [ ] All 39 tests pass

## Notes

Also merge ServiceCollectionExtensions into MessagingTestHarnessExtensions while refactoring

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
