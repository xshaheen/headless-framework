# Repetitive Option Parsing in Parser.cs

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, quality, source-generator, code-duplication

---

## Problem Statement

`ParseGlobalOptions` contains 7 nearly identical if-blocks for parsing global options.

**Location:** `src/Framework.Generator.Primitives/Parser.cs:61-124`

```csharp
// Pattern repeats 7 times:
if (analyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.GenerateJsonConverters", out var jsonConverters)
    && bool.TryParse(jsonConverters, out var json))
{
    options.GenerateJsonConverters = json;
}

if (analyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.GenerateTypeConverters", out var typeConverters)
    && bool.TryParse(typeConverters, out var type))
{
    options.GenerateTypeConverters = type;
}
// ... 5 more similar blocks
```

**Why it matters:**
- ~60 lines of repetitive code
- Easy to make copy-paste errors
- Hard to maintain

---

## Proposed Solutions

### Option A: Helper Method (Recommended)
```csharp
static bool ParseBool(AnalyzerConfigOptionsProvider provider, string key)
{
    return provider.GlobalOptions.TryGetValue($"build_property.{key}", out var value)
        && bool.TryParse(value, out var result)
        && result;
}

// Usage:
options.GenerateJsonConverters = ParseBool(provider, "GenerateJsonConverters");
options.GenerateTypeConverters = ParseBool(provider, "GenerateTypeConverters");
// ...
```
- **Pros:** ~40 LOC saved, clearer intent
- **Cons:** Minor indirection
- **Effort:** Small
- **Risk:** None

### Option B: Dictionary-Based Mapping
Map property names to actions/setters.
- **Pros:** Even more concise
- **Cons:** More complex, harder to debug
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Simple helper method.

---

## Technical Details

**Affected Files:**
- `src/Framework.Generator.Primitives/Parser.cs` (lines 61-124)

**Estimated LOC reduction:** ~40 lines

---

## Acceptance Criteria

- [ ] Repetitive parsing consolidated
- [ ] All 7 options still parse correctly
- [ ] All tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code-simplicity code review |
