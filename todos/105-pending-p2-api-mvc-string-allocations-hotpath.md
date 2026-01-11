---
status: pending
priority: p2
issue_id: "105"
tags: [code-review, dotnet, performance, api-mvc]
dependencies: []
---

# String Allocations in Hot Path - Accept Header Checking

## Problem Statement

The `CanAccept` method in `HttpRequestExtensions` is called on **every request** in the exception filter. It uses `StringValues.ToString()` which allocates a new string, and `InvariantCultureIgnoreCase` comparison which is 2-3x slower than `OrdinalIgnoreCase`.

## Findings

**Source:** performance-oracle agent

**Location:** `src/Framework.Api/Extensions/Http/HttpRequestExtensions.cs:65,87`

```csharp
var acceptHeaderText = acceptHeader.ToString();  // Allocates string

acceptHeader.ToString().Contains(contentType, StringComparison.InvariantCultureIgnoreCase)
// InvariantCultureIgnoreCase is slower than OrdinalIgnoreCase
```

**Impact:** ~50-200 bytes allocated per request (depending on Accept header size)

## Proposed Solutions

### Option 1: Use Span-Based Comparison (Recommended)
**Pros:** Zero allocation, faster comparison
**Cons:** Slightly more code
**Effort:** Medium
**Risk:** Low

```csharp
foreach (var value in acceptHeader)
{
    if (value.AsSpan().Contains(contentType.AsSpan(), StringComparison.OrdinalIgnoreCase))
        return true;
}
```

### Option 2: Just Fix Comparison Type
**Pros:** Simple change
**Cons:** Still allocates
**Effort:** Small
**Risk:** Low

Change `InvariantCultureIgnoreCase` to `OrdinalIgnoreCase`.

## Acceptance Criteria

- [ ] No string allocation in CanAccept hot path
- [ ] Uses OrdinalIgnoreCase
- [ ] Tests pass
- [ ] Benchmark shows improvement

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Hot path optimization opportunity |

## Resources

- HttpRequestExtensions.cs
