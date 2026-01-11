---
status: pending
priority: p3
issue_id: "111"
tags: [code-review, dotnet, api-mvc, duplication, refactor]
dependencies: []
---

# Unify BlockInEnvironment + RequireEnvironment Attributes

## Problem Statement

`BlockInEnvironmentAttribute` and `RequireEnvironmentAttribute` are nearly identical (98% same code) - they differ only by the condition being inverted (`!env.IsEnvironment` vs `env.IsEnvironment`).

## Findings

**Source:** code-simplicity-reviewer, pattern-recognition-specialist agents

**Files:**
- `src/Framework.Api.Mvc/Filters/BlockInEnvironmentAttribute.cs` (34 LOC)
- `src/Framework.Api.Mvc/Filters/RequireEnvironmentAttribute.cs` (34 LOC)

```csharp
// BlockInEnvironmentAttribute
if (!env.IsEnvironment(Environment)) { await next(); return; }

// RequireEnvironmentAttribute
if (env.IsEnvironment(Environment)) { await next(); return; }
```

## Proposed Solutions

### Option 1: Single Attribute with Parameter (Recommended)
**Pros:** Removes duplication, cleaner API
**Cons:** API change
**Effort:** Medium
**Risk:** Medium (breaking change)

```csharp
public sealed class EnvironmentFilterAttribute(string environment, bool blockInEnvironment = false)
    : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(...)
    {
        var isMatch = env.IsEnvironment(environment);
        if (isMatch != blockInEnvironment)
        {
            await next().AnyContext();
            return;
        }
        // Return 404...
    }
}
```

### Option 2: Extract Shared Base Class
**Pros:** Keeps existing API, reduces duplication
**Cons:** Still two classes
**Effort:** Small
**Risk:** Low

## Acceptance Criteria

- [ ] Code duplication eliminated
- [ ] Backward compatibility maintained or migration documented
- [ ] Tests pass

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from code review | Near-duplicate classes identified |

## Resources

- BlockInEnvironmentAttribute.cs
- RequireEnvironmentAttribute.cs
