---
status: pending
priority: p1
issue_id: "053"
tags: ["code-review","security","performance"]
dependencies: []
---

# RegisterKnownGroups never called — OTel cardinality guard is dead code

## Problem Statement

RegisterKnownGroups sets _knownGroups which gates the OTel cardinality guard and no-op state path. Zero call sites exist in the codebase — _knownGroups is always null at runtime. Any attacker-controlled Headers.Group value creates unbounded ConcurrentDictionary growth and OTel cardinality explosion → OOM.

## Findings

- **Location:** CircuitBreakerStateManager.cs:63
- **Risk:** Critical — unbounded memory growth + OTel collector crash
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, security-sentinel, learnings-researcher

## Proposed Solutions

### Call RegisterKnownGroups from ConsumerRegister.ExecuteAsync
- **Pros**: Groups already known after groupingMatches loop
- **Cons**: None significant
- **Effort**: Small
- **Risk**: Low

### Add hard cap on _groups.Count (defense-in-depth)
- **Pros**: Backstop independent of startup ordering
- **Cons**: Slightly more code
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Call RegisterKnownGroups from ExecuteAsync after groups are wired, AND add a hard cap (e.g., 1000) on _groups.Count.

## Acceptance Criteria

- [ ] RegisterKnownGroups called at startup with all known group names
- [ ] Unknown groups return no-op state (not added to dictionary)
- [ ] OTel metrics use _unknown tag for unrecognized groups
- [ ] Hard cap on _groups.Count with warning log

## Notes

Flagged by 4/7 review agents. Prior review (2026-03-21) also identified this as P1.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
