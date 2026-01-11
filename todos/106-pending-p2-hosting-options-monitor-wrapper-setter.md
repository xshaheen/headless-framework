# OptionsMonitorWrapper.CurrentValue Has Public Setter

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, quality, dotnet, hosting, options

---

## Problem Statement

`OptionsMonitorWrapper<TOptions>.CurrentValue` has a public setter, exposing mutable state.

```csharp
// Line 14
public TOptions CurrentValue { get; set; } = options;
```

Since `IOptionsMonitor<TOptions>.CurrentValue` only requires a getter, this exposes mutable state that could lead to surprising behavior if someone modifies it externally.

**Why it matters:**
- `IOptionsMonitor<T>` is typically readonly
- External mutation could cause inconsistent state
- If intentional for testing, should be documented

---

## Proposed Solutions

### Option A: Make Setter Init-Only
```csharp
public TOptions CurrentValue { get; init; } = options;
```
- **Pros:** Immutable after construction
- **Cons:** Breaking change if tests mutate
- **Effort:** Small
- **Risk:** Medium

### Option B: Make Setter Private
```csharp
public TOptions CurrentValue { get; private set; } = options;
```
- **Pros:** Still allows internal mutation if needed
- **Cons:** Slightly less restrictive than init
- **Effort:** Small
- **Risk:** Low

### Option C: Document as Testing-Only
- Add XML doc explaining this is for testing
- Keep public setter for test flexibility
- **Pros:** Clear intent
- **Cons:** Mutable state remains
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option B** - Make setter private. If tests need to mutate, they can create new instance.

---

## Technical Details

**Affected Files:**
- `src/Framework.Hosting/Options/OptionsMonitorWrapper.cs` (line 14)

---

## Acceptance Criteria

- [ ] Setter is no longer public
- [ ] Tests still work (may need adjustment)

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, security-sentinel |
