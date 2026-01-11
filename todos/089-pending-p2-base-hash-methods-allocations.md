# Hash Methods Create Per-Byte String Allocations

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, dotnet

---

## Problem Statement

`ToMd5`, `ToSha256`, `ToSha512` create string allocations per byte:

```csharp
public static string ToMd5(this string str)
{
    var data = MD5.HashData(Encoding.UTF8.GetBytes(str));  // Byte array allocation
    var sb = new StringBuilder();  // StringBuilder allocation

    foreach (var d in data)
    {
        sb.Append(d.ToString("X2", ...));  // STRING ALLOCATION PER BYTE!
    }
    return sb.ToString();
}
```

**Why it matters:**
- 16-64 string allocations per hash (one per byte)
- Plus StringBuilder allocation
- .NET 5+ has `Convert.ToHexString()` - single allocation

---

## Proposed Solutions

### Option A: Use Convert.ToHexString
```csharp
public static string ToMd5(this string str)
{
    var data = MD5.HashData(Encoding.UTF8.GetBytes(str));
    return Convert.ToHexString(data);  // Single allocation!
}

public static string ToSha256(this string str)
{
    var data = SHA256.HashData(Encoding.UTF8.GetBytes(str));
    return Convert.ToHexString(data).ToLowerInvariant();  // Match current lowercase output
}
```
- **Pros:** Dramatically fewer allocations, simpler code
- **Cons:** Uppercase by default (MD5 already uppercase, SHA needs ToLower for compat)
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Use `Convert.ToHexString()` for all hash methods.

Note: Also consider deprecating `ToMd5` as MD5 is cryptographically broken.

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/Core/StringExtensions.cs` (lines 732-776)
- `src/Framework.Base/IO/StreamExtensions.cs` (lines 237-253)

Also note inconsistency: MD5 uses "X2" (uppercase), SHA256/512 use "x2" (lowercase).

---

## Acceptance Criteria

- [ ] All hash methods use `Convert.ToHexString()`
- [ ] Output format matches existing (lowercase for SHA, uppercase for MD5)
- [ ] ZString StringBuilder usage removed

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle |
