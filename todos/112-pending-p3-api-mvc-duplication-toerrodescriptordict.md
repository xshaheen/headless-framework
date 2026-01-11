---
status: pending
priority: p3
issue_id: "112"
tags: [code-review, dotnet, api-mvc, duplication]
dependencies: []
---

# Code Duplication _ToErrorDescriptorDict Between Mvc and MinimalApi

## Problem Statement

The `_ToErrorDescriptorDict` method is duplicated verbatim between `Framework.Api.Mvc` and `Framework.Api.MinimalApi`:

## Findings

**Source:** pattern-recognition-specialist agent

**Locations:**
- `src/Framework.Api.Mvc/Extensions/ApiResultMvcExtensions.cs:70-77`
- `src/Framework.Api.MinimalApi/Extensions/ApiResultExtensions.cs:53-60`

Both implementations are identical:
```csharp
private static Dictionary<string, List<ErrorDescriptor>> _ToErrorDescriptorDict(ValidationError e)
{
    return e.FieldErrors.ToDictionary(
        kv => kv.Key,
        kv => kv.Value.Select(msg => new ErrorDescriptor($"validation:{kv.Key}", msg)).ToList(),
        StringComparer.Ordinal
    );
}
```

## Proposed Solutions

### Option 1: Move to Framework.Api or Framework.Primitives (Recommended)
**Pros:** Single source of truth
**Cons:** Requires identifying common package
**Effort:** Small
**Risk:** Low

### Option 2: Create Shared Internal Class
**Pros:** Simple
**Cons:** Still some indirection
**Effort:** Small
**Risk:** Low

## Acceptance Criteria

- [ ] Single implementation exists
- [ ] Both Mvc and MinimalApi use shared code
- [ ] Tests pass

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | DRY violation across packages |

## Resources

- ApiResultMvcExtensions.cs
- ApiResultExtensions.cs
