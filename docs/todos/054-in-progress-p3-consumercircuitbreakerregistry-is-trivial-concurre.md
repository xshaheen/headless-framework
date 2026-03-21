---
status: wont-fix
priority: p3
issue_id: "054"
tags: ["code-review","architecture"]
dependencies: []
---

# ConsumerCircuitBreakerRegistry is trivial ConcurrentDictionary wrapper

## Problem Statement

ConsumerCircuitBreakerRegistry wraps ConcurrentDictionary<string, ConsumerCircuitBreakerOptions> with thin delegate methods (Register, RegisterOrUpdate, Remove, TryGet). Zero added behavior. Adds a file, a DI registration, and indirection for no gain.

## Findings

- **Location:** ConsumerCircuitBreakerRegistry.cs
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Replace with plain ConcurrentDictionary or inline into CircuitBreakerStateManager.

## Acceptance Criteria

- [x] Registry class removed or justified

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Won't Fix

**By:** Agent
**Actions:**
- Status changed: in-progress → wont-fix
- **Justification:** The registry is not a trivial wrapper. It provides:
  1. **Duplicate detection** — `Register()` throws `InvalidOperationException` with a clear message on duplicate group names, preventing silent overwrites.
  2. **Startup/runtime bridge** — The registry is populated during `AddHeadlessMessaging()` (before ServiceProvider exists) by `ConsumerBuilder` and `Setup._DiscoverCircuitBreakerRegistrationsFromDI`, then consumed at runtime by `CircuitBreakerStateManager` via DI. Inlining into the state manager is impossible because the state manager is DI-resolved after configuration completes.
  3. **Named DI identity** — Registering a raw `ConcurrentDictionary<string, ConsumerCircuitBreakerOptions>` as a singleton loses semantic clarity and risks collisions with other dictionary registrations.
  4. **Encapsulated comparer** — `StringComparer.Ordinal` is set once in the registry constructor rather than relied upon at each call site.
