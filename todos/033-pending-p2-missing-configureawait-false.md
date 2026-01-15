---
status: pending
priority: p2
issue_id: 033
tags: [code-review, async-await, library-code, pr-146]
dependencies: []
---

# Missing ConfigureAwait(false) in Library Code

## Problem Statement

PR #146 code is library code (NuGet package) consumed by applications with various synchronization contexts (ASP.NET, WinForms, WPF, console). Missing `ConfigureAwait(false)` on async operations can cause deadlocks when called from single-threaded sync contexts and adds unnecessary context capture overhead.

**File:** `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs`

## Findings

### From strict-dotnet-reviewer

Missing `ConfigureAwait(false)` at:
- Lines 72, 96, 122: `await _syncSemaphore.LockAsync(cancellationToken)`
- Line 154: `await _GetOrSetDistributedCacheStampAsync(cancellationToken)`
- Line 163: `await _UpdateInMemoryStoreCacheAsync(cancellationToken)`
- Lines 203-204: Repository calls

**Current DynamicSettingDefinitionStore** (reference pattern) uses `.AnyContext()` extension (equivalent to `ConfigureAwait(false)`).

**Example from Settings (line 64):**
```csharp
using (await _syncSemaphore.LockAsync(cancellationToken).AnyContext())
```

**Features store (line 72):**
```csharp
using (await _syncSemaphore.LockAsync(cancellationToken))  // Missing .AnyContext()
```

### From pragmatic-dotnet-reviewer

Library code MUST use `ConfigureAwait(false)` to avoid:
1. **Deadlock risk** in UI applications (WPF/WinForms single-threaded sync context)
2. **Performance overhead** from unnecessary context capture
3. **Inconsistency** with established pattern (Settings store uses `.AnyContext()`)

### From pattern-recognition-specialist

**Inconsistency identified:**
- Settings store: 64 usages of `.AnyContext()`
- Features store: 0 usages
- Permissions store: 0 usages

This indicates Features/Permissions deviated from established pattern.

## Proposed Solutions

### Solution 1: Add .AnyContext() Extension (Recommended)

**Pros:**
- Matches Settings store pattern
- Shorter syntax than `ConfigureAwait(false)`
- Framework already has extension defined

**Cons:**
- Requires `using Framework.Base` import
- Custom extension (not BCL)

**Effort:** Small (1-2 hours)
**Risk:** Very Low

**Implementation:**
```csharp
using (await _syncSemaphore.LockAsync(cancellationToken).AnyContext())
{
    await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken).AnyContext();
    var cache = _featureMemoryCache;
    return cache.GetValueOrDefault(name);
}
```

### Solution 2: Use ConfigureAwait(false) Directly

**Pros:**
- Standard .NET pattern
- No custom extensions needed
- Clear intent

**Cons:**
- More verbose
- Inconsistent with Settings store

**Effort:** Small (1-2 hours)
**Risk:** Very Low

**Implementation:**
```csharp
using (await _syncSemaphore.LockAsync(cancellationToken).ConfigureAwait(false))
{
    await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken).ConfigureAwait(false);
    // ...
}
```

### Solution 3: No Action (Not Recommended)

**Pros:**
- No code changes

**Cons:**
- Deadlock risk in UI apps
- Performance overhead
- Inconsistent with codebase

**Effort:** None
**Risk:** High (deadlock in UI scenarios)

## Recommended Action

**IMPLEMENT SOLUTION 1** - Use `.AnyContext()` extension for consistency with Settings store.

- Add to all async calls in Features store
- Backport to Permissions store (same issue)
- Establish as standard pattern for all dynamic stores

## Technical Details

### Affected Files
- `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs`

### Locations Requiring .AnyContext()
- Lines 72, 96, 122: SemaphoreSlim locks
- Line 154: `_GetOrSetDistributedCacheStampAsync`
- Line 163: `_UpdateInMemoryStoreCacheAsync`
- Lines 171, 191: `distributedCache.GetAsync`
- Line 178: `resourceLockProvider.TryAcquireAsync`
- Line 198: `_ChangeCommonStampAsync`
- Lines 203-204: `repository.GetGroupsListAsync`, `GetFeaturesListAsync`
- Lines 304, 323, 339: `SaveAsync` method calls
- Many more in save logic

**Total:** ~20-30 await calls need `.AnyContext()`

### Reference Implementation
`src/Framework.Settings.Core/Definitions/DynamicSettingDefinitionStore.cs` - consistent `.AnyContext()` usage throughout

## Acceptance Criteria

- [ ] Add `.AnyContext()` to all awaits in `DynamicFeatureDefinitionStore`
- [ ] Verify `using Framework.Base;` import exists
- [ ] Compare against Settings store for missed locations
- [ ] Add analyzer rule to catch missing `ConfigureAwait` in library projects
- [ ] Code compiles and tests pass
- [ ] Test in WinForms/WPF context to verify no deadlocks

## Work Log

### 2026-01-15
- **Discovered:** Code review identified missing `ConfigureAwait` in library code
- **Compared:** Settings store consistently uses `.AnyContext()`, Features doesn't
- **Impact:** P2 - deadlock risk in UI applications, performance overhead

## Resources

- PR: #146
- File: `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs`
- Reference: `src/Framework.Settings.Core/Definitions/DynamicSettingDefinitionStore.cs`
- .NET Docs: [ConfigureAwait FAQ](https://devblogs.microsoft.com/dotnet/configureawait-faq/)
- Extension: `Framework.Base` namespace (already in codebase)
