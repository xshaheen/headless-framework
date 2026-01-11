# AddIf/UseIf Code Duplication Across Extensions

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-have
**Tags:** code-review, quality, dotnet, hosting, duplication

---

## Problem Statement

The `AddIf`/`UseIf` and `AddIfElse`/`UseIfElse` pattern is copy-pasted across 4 files with near-identical implementations:

| File | Lines |
|------|-------|
| `ConfigurationBuilderExtensions.cs` | 21-63 |
| `DependencyInjectionExtensions.cs` | 24-64 |
| `LoggingBuilderExtensions.cs` | 23-63 |
| `HostBuilderExtensions.cs` | 23-115 |

Each contains essentially:
```csharp
if (condition) { builder = action(builder); }
return builder;
```

**Additional issue:** `HostBuilderExtensions` has 4 overloads (2 with `Func<IHostBuilder, bool>` condition) while others only have 2. API surface inconsistency.

**Pragmatic view (from Scott Hanselman perspective):** These are abstractions over `if` statements. Just use `if`.

---

## Proposed Solutions

### Option A: Delete All, Use Native If
```csharp
// Instead of:
services.AddIf(isDevelopment, s => s.AddSomething());

// Just write:
if (isDevelopment) services.AddSomething();
```
- **Pros:** Simpler, no framework dependency, junior-friendly
- **Cons:** Breaking change, some prefer fluent style
- **Effort:** Medium (need to update consumers)
- **Risk:** High (breaking)

### Option B: Keep But Document Trade-offs
- These enable fluent chaining
- Document they're syntactic sugar
- **Pros:** No breaking change
- **Cons:** Duplication remains
- **Effort:** Small
- **Risk:** Low

### Option C: Add Missing Overloads for Consistency
- Add `Func<T, bool>` condition overloads to other extension classes
- **Pros:** Consistent API surface
- **Cons:** More duplication
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option B** - Keep as-is. The fluent API has value for some consumers. Low priority.

---

## Technical Details

**Affected Files:**
- `src/Framework.Hosting/Configuration/ConfigurationBuilderExtensions.cs`
- `src/Framework.Hosting/DependencyInjection/DependencyInjectionExtensions.cs`
- `src/Framework.Hosting/Logging/LoggingBuilderExtensions.cs`
- `src/Framework.Hosting/Hosting/HostBuilderExtensions.cs`

---

## Acceptance Criteria

- [ ] Decision made on whether to keep or remove
- [ ] If kept, consider adding missing overloads for consistency

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pragmatic-dotnet-reviewer, pattern-recognition-specialist |
