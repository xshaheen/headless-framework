# SequentialGuid Should Use Guid.CreateVersion7

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, modernization, dotnet

---

## Problem Statement

`SequentialGuid.cs` contains 111 lines of custom bit-twiddling for sequential GUID generation, but .NET 9+ has `Guid.CreateVersion7()`:

```csharp
// Current: Custom implementation with endianness handling
private static byte[] _GetNextSequentialAsBinaryBytes()
{
    var randomBytes = Guid.NewGuid().ToByteArray();
    var timestamp = DateTime.UtcNow.Ticks / 10000L;
    var timestampBytes = BitConverter.GetBytes(timestamp);
    if (BitConverter.IsLittleEndian) { Array.Reverse(timestampBytes); }
    // ... more manual byte manipulation
}
```

**Why it matters:**
- .NET 9 added `Guid.CreateVersion7()` - RFC 9562 compliant UUIDv7
- Time-ordered, database-index-friendly (same benefits)
- Built into runtime, battle-tested
- Custom implementation has security concerns (predictable counter)

---

## Proposed Solutions

### Option A: Replace with Guid.CreateVersion7()
```csharp
public static Guid NextSequentialAsString() => Guid.CreateVersion7();
public static Guid NextSequentialAsBinary() => Guid.CreateVersion7();

[Obsolete("Use Guid.CreateVersion7() instead")]
public static Guid NextSequentialAtEnd() => /* keep for SQL Server compat */
```
- **Pros:** Uses standard, less code, more secure
- **Cons:** `NextSequentialAtEnd` for SQL Server may need retention
- **Effort:** Small
- **Risk:** Low

### Option B: Deprecate Entire Class
```csharp
[Obsolete("Use Guid.CreateVersion7() directly. This class will be removed.")]
public static class SequentialGuid { ... }
```
- **Pros:** Encourages BCL usage
- **Cons:** Breaking change
- **Effort:** Small
- **Risk:** Medium

---

## Recommended Action

**Option A** - Replace `NextSequentialAsString` and `NextSequentialAsBinary` with `Guid.CreateVersion7()`. Keep `NextSequentialAtEnd` for SQL Server compatibility but mark obsolete with migration guidance.

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/Core/SequentialGuid.cs` (entire file)

---

## Acceptance Criteria

- [ ] `NextSequentialAsString` uses `Guid.CreateVersion7()`
- [ ] `NextSequentialAsBinary` uses `Guid.CreateVersion7()`
- [ ] `NextSequentialAtEnd` marked obsolete with migration docs
- [ ] Document which databases need which method

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pragmatic-dotnet-reviewer, security-sentinel |
