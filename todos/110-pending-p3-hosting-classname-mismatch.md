# Class Name Mismatch: HostBuilderHelperExtensions

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-have
**Tags:** code-review, quality, naming, hosting

---

## Problem Statement

File `HostBuilderExtensions.cs` contains class `HostBuilderHelperExtensions`. Other files follow pattern `{Type}Extensions`.

```csharp
// File: HostBuilderExtensions.cs
// Line 13
public static class HostBuilderHelperExtensions  // Should be HostBuilderExtensions
```

**Inconsistent with:**
- `ConfigurationBuilderExtensions.cs` -> `ConfigurationBuilderExtensions`
- `DependencyInjectionExtensions.cs` -> `DependencyInjectionExtensions`
- `LoggingBuilderExtensions.cs` -> `LoggingBuilderExtensions`

---

## Proposed Solutions

### Option A: Rename Class to Match File
```csharp
public static class HostBuilderExtensions
```
- **Pros:** Consistent naming
- **Cons:** Technically breaking if external code references by name
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Rename to `HostBuilderExtensions`.

---

## Technical Details

**Affected Files:**
- `src/Framework.Hosting/Hosting/HostBuilderExtensions.cs` (line 13)

---

## Acceptance Criteria

- [ ] Class name matches filename
- [ ] Consistent with other extension classes

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pattern-recognition-specialist |
