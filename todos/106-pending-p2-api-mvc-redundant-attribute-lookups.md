---
status: pending
priority: p2
issue_id: "106"
tags: [code-review, dotnet, performance, api-mvc]
dependencies: []
---

# Redundant Attribute Lookups in RedirectToCanonicalUrlRule

## Problem Statement

`RedirectToCanonicalUrlRule._TryGetCanonicalUrl()` calls `_HasAttribute<NoTrailingSlashAttribute>()` twice per request (lines 94 and 113), causing redundant endpoint metadata lookups.

## Findings

**Source:** performance-oracle agent

**Location:** `src/Framework.Api.Mvc/Middlewares/RedirectToCanonicalUrlRule.cs:94,113,126`

```csharp
if (!hasTrailingSlash && !_HasAttribute<NoTrailingSlashAttribute>(context))  // Line 94
// ...
if (LowercaseUrls && !_HasAttribute<NoTrailingSlashAttribute>(context))  // Line 113 - DUPLICATE
// ...
if (!_HasAttribute<NoLowercaseQueryStringAttribute>(context))  // Line 126
```

`_HasAttribute<T>()` calls `endpoint?.Metadata.GetMetadata<T>()` which is O(n) where n = metadata count.

## Proposed Solutions

### Option 1: Cache Attribute Checks at Method Start (Recommended)
**Pros:** Single lookup per attribute type
**Cons:** None
**Effort:** Small
**Risk:** Low

```csharp
private bool _TryGetCanonicalUrl(RewriteContext context, out Uri? canonicalUrl)
{
    var hasNoTrailingSlash = _HasAttribute<NoTrailingSlashAttribute>(context);
    var hasNoLowercaseQueryString = _HasAttribute<NoLowercaseQueryStringAttribute>(context);

    // Use cached values below...
}
```

## Acceptance Criteria

- [ ] Each attribute type looked up once per request
- [ ] Behavior unchanged
- [ ] Tests pass

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Cache repeated lookups |

## Resources

- RedirectToCanonicalUrlRule.cs
