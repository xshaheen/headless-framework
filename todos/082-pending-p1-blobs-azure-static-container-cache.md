# Static Container Cache - Memory Leak and Race Condition

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, dotnet, blobs-azure, memory-leak, concurrency, architecture

---

## Problem Statement

`AzureBlobStorage` uses a static `ConcurrentDictionary` to cache created containers:

```csharp
private static readonly ConcurrentDictionary<string, bool> _CreatedContainers = new(StringComparer.Ordinal);
```

**Issues:**

1. **Memory Leak**: Dictionary grows unbounded, never cleared. In long-running apps with dynamic container names, this consumes memory indefinitely.

2. **Race Condition (TOCTOU)**: Check-then-act pattern at lines 57-69:
```csharp
if (_CreatedContainers.ContainsKey(blobContainer)) { return; }  // Check
// ...concurrent call can pass here too...
await containerClient.CreateIfNotExistsAsync(...);
_CreatedContainers.TryAdd(blobContainer, value: true);  // Act
```

3. **Cross-Instance Pollution**: Static = shared across all instances. Multi-tenant scenarios with different storage accounts share this cache incorrectly.

4. **Stale Cache**: If container deleted externally, cache is stale forever.

5. **Test Pollution**: Static state persists across test runs.

**Why it matters:**
- At 1000 containers with avg 50-char names: ~100KB leak
- At 100K containers: ~10MB leak that never reclaims
- Multi-tenant apps may see incorrect behavior

---

## Proposed Solutions

### Option A: Remove Caching, Rely on SDK Idempotency
```csharp
// Remove _CreatedContainers entirely
// CreateIfNotExistsAsync is already idempotent
await containerClient.CreateIfNotExistsAsync(...);
```
- **Pros:** Simplest, no stale cache issues
- **Cons:** Extra API call per container per instance lifetime
- **Effort:** Small
- **Risk:** Low

### Option B: Instance-Scoped Cache
```csharp
private readonly ConcurrentDictionary<string, bool> _createdContainers = new(...);
```
- **Pros:** No cross-instance pollution
- **Cons:** Still unbounded, doesn't fix race condition
- **Effort:** Small
- **Risk:** Low

### Option C: Time-Limited Cache (MemoryCache)
```csharp
private readonly IMemoryCache _containerCache;
// Set 1-hour expiration
```
- **Pros:** Bounded, handles external deletion
- **Cons:** Adds dependency, more complex
- **Effort:** Medium
- **Risk:** Low

### Option D: GetOrAdd Pattern (Fix Race Condition Only)
```csharp
await _CreatedContainers.GetOrAddAsync(blobContainer, async _ =>
{
    await containerClient.CreateIfNotExistsAsync(...);
    return true;
});
```
- **Pros:** Fixes race condition
- **Cons:** Still has memory leak, cross-instance issues
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Remove caching entirely. The Azure SDK's `CreateIfNotExistsAsync` is idempotent and the performance gain from caching is minimal compared to the complexity and bugs it introduces.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (lines 24, 57-70)
- Same pattern in `src/Framework.Blobs.Aws/AwsBlobStorage.cs` (line 24)

---

## Acceptance Criteria

- [ ] Static `_CreatedContainers` removed
- [ ] `CreateContainerAsync` calls SDK directly
- [ ] Tests pass in parallel execution
- [ ] No memory leak in long-running scenario

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
