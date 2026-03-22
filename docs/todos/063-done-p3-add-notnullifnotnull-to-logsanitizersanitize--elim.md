---
status: done
priority: p3
issue_id: "063"
tags: ["code-review","quality"]
dependencies: []
---

# Add [NotNullIfNotNull] to LogSanitizer.Sanitize — eliminate noisy null-coalescing at call sites

## Problem Statement

LogSanitizer.Sanitize returns 'string?' and only returns null when input is null. All call sites where input is known non-null add unnecessary '?? fallback' expressions (e.g. 'LogSanitizer.Sanitize(groupName) ?? string.Empty' in ResetAsync, '?? groupName' in _GetOrAddState). Adding [return: NotNullIfNotNull("value")] to the method signature tells the nullable analyzer that null-in → null-out and non-null-in → non-null-out, eliminating the need for defensive coalescing at call sites.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/LogSanitizer.cs:25
- **Discovered by:** simplicity-reviewer (P2)

## Proposed Solutions

### Add [return: NotNullIfNotNull("value")] to LogSanitizer.Sanitize
- **Pros**: Removes noisy null-coalescing from ~6 call sites
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add the attribute and remove the now-redundant '?? fallback' expressions at call sites where the input is known non-null.

## Acceptance Criteria

- [ ] [return: NotNullIfNotNull] added to Sanitize
- [ ] Redundant null-coalescing removed from call sites
- [ ] Build compiles cleanly without new nullable warnings

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
