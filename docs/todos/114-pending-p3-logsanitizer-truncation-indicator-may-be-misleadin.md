---
status: pending
priority: p3
issue_id: "114"
tags: ["code-review","dotnet","correctness"]
dependencies: []
---

# LogSanitizer truncation indicator may be misleading

## Problem Statement

When needsTruncation is true but stripped characters are dense, output can be shorter than expected. The '...' suffix is appended even when pos < effectiveMax, implying content was cut when it may have fit after stripping.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/LogSanitizer.cs:39-84
- **Discovered by:** compound-engineering:review:security-sentinel

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Check whether content was actually truncated before appending '...' suffix.

## Acceptance Criteria

- [ ] Truncation suffix only appended when content was actually cut

## Notes

Source: Code review

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
