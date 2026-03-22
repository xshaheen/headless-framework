---
status: done
priority: p2
issue_id: "024"
tags: ["code-review","threading"]
dependencies: []
---

# Add Volatile.Write/Read for _knownGroups assignment (ARM64 visibility)

## Problem Statement

RegisterKnownGroups (CircuitBreakerStateManager.cs:85) assigns _knownGroups via plain write. _GetOrAddState (line 463) reads it without Volatile.Read. On ARM64 (weakly-ordered architecture), the write may not be visible to threads running _GetOrAddState without a memory barrier.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:82-95,463
- **Risk:** Medium — ARM64 visibility issue; x86 is fine due to strong ordering
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Use Volatile.Write in RegisterKnownGroups and Volatile.Read in _GetOrAddState for the _knownGroups field.

## Acceptance Criteria

- [ ] _knownGroups written with Volatile.Write
- [ ] _knownGroups read with Volatile.Read at all consumption sites

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
