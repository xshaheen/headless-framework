# StreamExtensions.GetAllBytesAsync Returns Wrong Buffer

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, bug, io, dotnet

---

## Problem Statement

`GetAllBytesAsync` has two bugs (lines 107-119):

```csharp
public static async Task<byte[]> GetAllBytesAsync(this Stream stream, CancellationToken cancellationToken = default)
{
    Argument.IsNotNull(stream);

    if (stream is MemoryStream s)
    {
        return s.ToArray();  // BUG 1: Doesn't reset position first!
    }

    await using var ms = await stream.CreateMemoryStreamAsync(cancellationToken);
    return ms.GetBuffer();  // BUG 2: Returns internal buffer, not actual data!
}
```

**Bug 1:** When input is `MemoryStream`, position isn't reset - may return incomplete data
**Bug 2:** `GetBuffer()` returns the internal buffer which may be LARGER than actual content

Compare with sync version (lines 89-104) which correctly calls `stream.ResetPosition()`.

**Why it matters:**
- Returns corrupted/oversized data
- Buffer may contain garbage bytes beyond actual content
- Inconsistent behavior between sync and async versions

---

## Proposed Solutions

### Option A: Fix Both Issues
```csharp
public static async Task<byte[]> GetAllBytesAsync(this Stream stream, CancellationToken cancellationToken = default)
{
    Argument.IsNotNull(stream);

    stream.ResetPosition();  // Fix bug 1

    if (stream is MemoryStream s)
    {
        return s.ToArray();
    }

    await using var ms = await stream.CreateMemoryStreamAsync(cancellationToken);
    return ms.ToArray();  // Fix bug 2: Use ToArray() not GetBuffer()
}
```
- **Pros:** Correct behavior, matches sync version pattern
- **Cons:** `ToArray()` allocates new array
- **Effort:** Small
- **Risk:** Low

### Option B: Use GetBuffer with Length
```csharp
return ms.GetBuffer().AsSpan(0, (int)ms.Length).ToArray();
```
- **Pros:** Slightly more explicit about intent
- **Cons:** More verbose, same allocation
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Use `ToArray()` consistently. The comment "avoid extra array allocation" is wrong - `GetBuffer()` returns wrong data.

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/IO/StreamExtensions.cs` (lines 107-119)

---

## Acceptance Criteria

- [ ] Position is reset before reading
- [ ] Returns exactly the bytes in the stream, no garbage
- [ ] Behavior matches sync `GetAllBytes` method
- [ ] Unit test with partially-read MemoryStream

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
