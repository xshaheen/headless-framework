# Redis Blob Storage Regex Compiled on Every Search

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, dotnet, redis, blobs

---

## Problem Statement

New `Regex` instance created for every search operation (lines 570-571):

```csharp
var searchRegexText = Regex.Escape(searchPattern).Replace("\\*", ".*?", StringComparison.Ordinal);
patternRegex = new Regex($"^{searchRegexText}$", RegexOptions.ExplicitCapture, RegexPatterns.MatchTimeout);
```

**Performance Impact:**
- Regex compilation: ~50-200 microseconds
- Cached regex match: ~0.5-2 microseconds
- 100-1000x overhead on repeated searches

---

## Findings

**From performance-oracle:**
- CPU bound; becomes bottleneck at ~1000 searches/sec
- Memory pressure from Regex objects

**From strict-dotnet-reviewer:**
- Consider using `[GeneratedRegex]` source generator or caching

---

## Proposed Solutions

### Option A: ConcurrentDictionary Cache
```csharp
private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();

patternRegex = _regexCache.GetOrAdd(searchPattern, pattern =>
{
    var escaped = Regex.Escape(pattern).Replace("\\*", ".*?");
    return new Regex($"^{escaped}$", RegexOptions.ExplicitCapture, RegexPatterns.MatchTimeout);
});
```
- **Pros:** Simple, thread-safe
- **Cons:** Unbounded cache growth
- **Effort:** Small
- **Risk:** Low

### Option B: Bounded LRU Cache
```csharp
private static readonly LruCache<string, Regex> _regexCache = new(maxSize: 100);
```
- **Pros:** Memory bounded
- **Cons:** Requires LRU implementation or library
- **Effort:** Medium
- **Risk:** Low

### Option C: MemoryCache with Expiration
```csharp
_memoryCache.GetOrCreate(searchPattern, entry =>
{
    entry.SlidingExpiration = TimeSpan.FromMinutes(10);
    return new Regex(...);
});
```
- **Pros:** Auto-expiration, .NET built-in
- **Cons:** Requires IMemoryCache injection
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** with size limit check - simple ConcurrentDictionary with periodic or size-based eviction.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Redis/RedisBlobStorage.cs` (lines 568-577)

**Affected Methods:**
- `_GetRequestCriteria`

---

## Acceptance Criteria

- [ ] Cache compiled Regex instances
- [ ] Limit cache size to prevent memory leak
- [ ] Benchmark before/after (target: 10x improvement for repeated patterns)

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle |
