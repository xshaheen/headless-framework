# Unused Import: System.Text.RegularExpressions

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, cleanup, minimalapi, dotnet

---

## Problem Statement

In `MinimalApiExceptionFilter.cs` line 3, there is an unused import:

```csharp
using System.Text.RegularExpressions;  // UNUSED
```

This namespace is not used anywhere in the file.

**Why it matters:**
- Code noise
- May indicate incomplete refactoring
- Violates clean code principles

---

## Proposed Solutions

### Option A: Remove the Import
```csharp
// Delete line 3
```
- **Pros:** Clean code
- **Cons:** None
- **Effort:** Trivial
- **Risk:** None

---

## Recommended Action

**Option A** - Remove the unused import.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api.MinimalApi/Filters/MinimalApiExceptionFilter.cs` (line 3)

---

## Acceptance Criteria

- [ ] Unused import removed
- [ ] Code compiles without errors

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
