---
status: done
priority: p2
issue_id: "003"
tags: ["code-review","quality","dotnet","architecture"]
dependencies: []
---

# Fix explicit subscribe for multi-message consumers

## Problem Statement

Assembly scanning registers every closed IConsume<T> interface on a consumer, but the new explicit Subscribe<TConsumer>() and Subscribe<TConsumer>(string topic) overloads stop at the first IConsume<T> interface they find. Multi-message consumers therefore work when scanned but become incomplete and reflection-order-dependent when registered explicitly, despite the package docs advertising multi-type consumer support.

## Findings

- **Location:** src/Headless.Messaging.Core/Configuration/MessagingOptions.cs:230-279
- **Location:** src/Headless.Messaging.Abstractions/README.md:17-19
- **Location:** tests/Headless.Messaging.Core.Tests.Unit/MessagingBuilderTests.cs:262-281
- **Risk:** Medium - explicit registration drops message handlers for multi-message consumers
- **Discovered by:** pragmatic-dotnet-reviewer / code-simplicity-reviewer

## Proposed Solutions

### Register all closed IConsume<T> interfaces in explicit Subscribe overloads
- **Pros**: Keeps scan and explicit registration behavior consistent
- **Cons**: The builder API may need redesign for per-message overrides
- **Effort**: Medium
- **Risk**: Medium

### Fail fast for multi-message consumers in explicit Subscribe overloads
- **Pros**: Simplest contract, avoids partial registration
- **Cons**: Narrower API unless a dedicated multi-message registration surface is added
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Do not allow partial registration. Either register every closed interface explicitly or reject multi-message consumers on the explicit overloads.

## Acceptance Criteria

- [x] Explicit registration no longer partially registers multi-message consumers
- [x] A regression test covers explicit registration of a consumer implementing two IConsume<T> interfaces
- [x] Package docs no longer promise behavior that explicit registration cannot provide

## Notes

Discovered during PR #184 review

## Work Log

### 2026-03-11 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-12 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-12 - Fixed

**By:** Codex
**Actions:**
- Made explicit `Subscribe<TConsumer>()` overloads fail fast when a consumer implements multiple `IConsume<T>` interfaces
- Kept assembly scanning as the supported path for multi-message consumers
- Added regression tests and updated public docs to reflect the stricter explicit-registration contract

### 2026-03-12 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
