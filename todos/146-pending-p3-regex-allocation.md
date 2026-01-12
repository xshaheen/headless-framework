---
status: pending
priority: p3
issue_id: "146"
tags: [code-review, blobs, aws, performance]
dependencies: []
---

# Regex Allocation in Hot Path

## Problem Statement

New Regex instance created for every `_GetRequestCriteria` call.

## Findings

- **Location:** `src/Framework.Blobs.Aws/AwsBlobStorage.cs:726`
```csharp
patternRegex = new Regex($"^{searchRegexText}$", RegexOptions.ExplicitCapture, RegexPatterns.MatchTimeout);
```
- Called on every list/delete with pattern
- Minor overhead, mitigated by timeout

## Proposed Solutions

### Option 1: Accept Current Implementation

Current implementation uses timeout and ExplicitCapture - reasonable.

**Effort:** 0 | **Risk:** None

### Option 2: Cache Common Patterns

Use ConcurrentDictionary to cache compiled patterns.

**Effort:** 1-2 hours | **Risk:** Medium (complexity)

## Acceptance Criteria

- [ ] Evaluate if pattern caching worthwhile
- [ ] Only implement if profiling shows bottleneck
