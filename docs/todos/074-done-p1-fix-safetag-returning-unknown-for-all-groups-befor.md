---
status: done
priority: p1
issue_id: "074"
tags: ["code-review","messaging","observability"]
dependencies: []
---

# Fix _SafeTag returning _unknown for all groups before RegisterKnownGroups is called

## Problem Statement

CircuitBreakerMetrics._SafeTag (line 82) returns UnknownGroupTag ('_unknown') for ALL group names when _knownGroups.Count == 0, which is the case during the entire startup window before RegisterKnownGroups is called. This causes every circuit trip metric and OTel gauge to be attributed to '_unknown' at startup, permanently losing attribution for the correct group name.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerMetrics.cs:82-85
- **Problem:** When _knownGroups.Count == 0, returns UnknownGroupTag for ALL groups including legitimate ones
- **Impact:** Pre-startup circuit trips attributed to '_unknown' — metrics permanently wrong
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, security-sentinel, performance-oracle

## Proposed Solutions

### Return groupName when guard not yet armed
- **Pros**: Simple 1-line fix, correct semantics (guard only blocks after armed)
- **Cons**: Slight cardinality risk if rogue groups arrive pre-startup (acceptable)
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change the early-out: when known.Count == 0, return groupName as-is rather than UnknownGroupTag. The cardinality guard only makes sense after the set is populated.

## Acceptance Criteria

- [ ] When _knownGroups is empty, _SafeTag returns groupName (not UnknownGroupTag)
- [ ] After RegisterKnownGroups, unknown names still return UnknownGroupTag
- [ ] Unit test added covering pre-registration and post-registration behavior

## Notes

PR #194 code review finding. Fix: `if (known.Count == 0) return groupName;`

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-23 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
