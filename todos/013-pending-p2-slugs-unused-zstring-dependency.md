# Unused ZString Dependency

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, dependencies, dotnet, slugs

---

## Problem Statement

`Framework.Slugs.csproj` references ZString but the code uses standard `StringBuilder`:

```xml
<!-- Framework.Slugs.csproj:6 -->
<PackageReference Include="ZString" />
```

```csharp
// Slug.cs:29 - uses regular StringBuilder, not ZString
var sb = new StringBuilder(textLength);
```

**Why it matters:**
- Every consumer pulls in ZString for no benefit
- Increased package size
- Potential version conflicts with other ZString consumers
- Dead dependency is confusing for maintainers

---

## Proposed Solutions

### Option A: Remove ZString Dependency
```xml
<!-- Delete line 6 from .csproj -->
```
- **Pros:** Cleaner, smaller package
- **Cons:** None
- **Effort:** Trivial
- **Risk:** None

### Option B: Actually Use ZString
```csharp
using var sb = ZString.CreateStringBuilder();
```
- **Pros:** Better perf via pooled builders
- **Cons:** API is slightly different
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Remove it. If perf becomes critical, add it back with actual usage.

---

## Technical Details

**Affected Files:**
- `src/Framework.Slugs/Framework.Slugs.csproj` (line 6)

---

## Acceptance Criteria

- [ ] ZString removed from .csproj
- [ ] Build succeeds
- [ ] Tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pragmatic-dotnet-reviewer, architecture-strategist |
