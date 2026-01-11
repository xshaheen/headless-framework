# Dead Code and Unused Enum Values

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, quality, source-generator, dead-code

---

## Problem Statement

Several pieces of dead code exist in the generator:

1. **`GeneratorStarted()` diagnostic never called**
   - Location: `src/Framework.Generator.Primitives/Helpers/DiagnosticHelper.cs:15-28`
   - 14 lines of unused code

2. **Unused `LineType` enum values**
   - Location: `src/Framework.Generator.Primitives/Shared/SourceCodeBuilder.cs:575-576`
   - `OpenParenthesis` and `CloseParenthesis` defined but never used in `_CheckIndent`

3. **Redundant null check after Where filter**
   - Location: `src/Framework.Generator.Primitives/Emitter.cs:48-51`
   ```csharp
   if (typeSymbol is null) // Will never happen
   {
       continue;
   }
   ```
   - Comment admits it's defensive; `Where(static x => x is not null)` already filters

**Why it matters:**
- Dead code adds cognitive load
- Misleading about what features exist
- Maintenance burden

---

## Proposed Solutions

### Option A: Remove All Dead Code (Recommended)
Delete the identified dead code:
- Remove `GeneratorStarted()` method entirely
- Remove unused enum values
- Remove redundant null check (or remove comment if keeping for safety)

- **Pros:** Clean codebase
- **Cons:** None
- **Effort:** Small
- **Risk:** None

---

## Recommended Action

**Option A** - Remove all dead code.

---

## Technical Details

**Affected Files:**
- `src/Framework.Generator.Primitives/Helpers/DiagnosticHelper.cs` (lines 15-28)
- `src/Framework.Generator.Primitives/Shared/SourceCodeBuilder.cs` (lines 575-576)
- `src/Framework.Generator.Primitives/Emitter.cs` (lines 48-51)

**Estimated LOC reduction:** ~20 lines

---

## Acceptance Criteria

- [ ] `GeneratorStarted()` removed
- [ ] Unused `LineType` enum values removed
- [ ] Redundant null check removed or comment updated
- [ ] All tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code-simplicity code review |
