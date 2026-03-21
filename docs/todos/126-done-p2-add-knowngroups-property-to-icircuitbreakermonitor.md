---
status: done
priority: p2
issue_id: "126"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Add KnownGroups property to ICircuitBreakerMonitor for group enumeration

## Problem Statement

ICircuitBreakerMonitor has no way to enumerate registered group names. GetAllStates() only returns groups accessed at runtime (lazy init). Before any messages are processed, the dictionary may be empty even with configured consumers. An agent cannot discover valid group names to pass to GetState() or ResetAsync() without processing messages first — context starvation.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:39 (_knownGroups, private)
- **Risk:** Agent cannot enumerate valid group names — cannot use GetState/ResetAsync without prior knowledge
- **Discovered by:** compound-engineering:review:agent-native-reviewer

## Proposed Solutions

### Add IReadOnlySet<string>? KnownGroups { get; } backed by _knownGroups field
- **Pros**: Exposes startup group registration; null when RegisterKnownGroups not called
- **Cons**: Minor API addition
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add KnownGroups property to ICircuitBreakerMonitor. Implement as _knownGroups in CircuitBreakerStateManager.

## Acceptance Criteria

- [ ] KnownGroups returns set from RegisterKnownGroups
- [ ] KnownGroups is null if RegisterKnownGroups not called
- [ ] Test verifies KnownGroups populated after registration

## Notes

PR #194 second-pass review.

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
