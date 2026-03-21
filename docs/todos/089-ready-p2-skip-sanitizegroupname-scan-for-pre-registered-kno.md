---
status: ready
priority: p2
issue_id: "089"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Skip _SanitizeGroupName scan for pre-registered known groups on hot path

## Problem Statement

_SanitizeGroupName is called on every single message received, performing a linear scan of up to 256 characters for control characters. At 100k messages/second with a 20-character group name, this is 2M character comparisons per second. After RegisterKnownGroups is called, the vast majority of group names are pre-validated trusted names from startup configuration.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs (~line 3619, _SanitizeGroupName at 3700)
- **Risk:** Unnecessary CPU overhead on every message — 2M char ops/sec at 100k msg/s
- **Discovered by:** performance-oracle

## Proposed Solutions

### Check _knownGroups.Contains(groupName) before calling _SanitizeGroupName
- **Pros**: O(1) HashSet lookup replaces O(N) char scan for trusted names
- **Cons**: Minor logic change
- **Effort**: Small
- **Risk**: Low


## Recommended Action

In the call site: skip scan for all registered groups by checking _knownGroups first. Only call _SanitizeGroupName for unknown/unregistered group names.

## Acceptance Criteria

- [ ] No char scan for groups registered in _knownGroups
- [ ] Char scan still applies for unknown/unregistered group names
- [ ] Benchmark added or existing benchmark updated to verify

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
