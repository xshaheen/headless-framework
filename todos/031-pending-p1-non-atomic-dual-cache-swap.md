---
status: pending
priority: p1
issue_id: 031
tags: [code-review, concurrency, data-integrity, pr-146]
dependencies: []
---

# Non-Atomic Dual Cache Swap in DynamicFeatureDefinitionStore

## Problem Statement

PR #146 swaps two volatile dictionaries (`_groupMemoryCache` and `_featureMemoryCache`) with separate assignments (lines 239-240), creating a race condition window where readers can see mismatched cache states (new groups + old features or vice versa). This violates referential integrity between features and their parent groups.

**File:** `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs:239-240`

## Findings

### From strict-dotnet-reviewer

Non-atomic dual-field swap creates visibility window:

```csharp
// Lines 238-240
_groupMemoryCache = newGroupCache.ToImmutable();  // Write 1
_featureMemoryCache = newFeatureCache.ToImmutable(); // Write 2
```

**Race scenario:**
1. Thread A writes new `_groupMemoryCache`
2. Thread B reads `_groupMemoryCache` (NEW) and `_featureMemoryCache` (OLD)
3. Thread A writes new `_featureMemoryCache`

**Result:** Feature lookup returns definition from old cache referencing group from new cache → potential null refs.

### From data-integrity-guardian

Features reference groups via `GroupName` (lines 223-235). During swap window:
- `GetGroupsAsync()` returns new groups
- `GetFeaturesAsync()` returns old features
- Feature's `feature.Group` may reference non-existent group in returned set

### From architecture-strategist

Violates atomicity assumption in hierarchical feature resolution. Parent-child relationships between features and groups require consistent snapshot view.

## Proposed Solutions

### Solution 1: Single Volatile Struct (Recommended)

**Pros:**
- Atomic swap via single reference assignment
- Eliminates race window completely
- Clean pattern, well-understood

**Cons:**
- Requires wrapper type
- Minor refactor to access pattern

**Effort:** Small (2-4 hours)
**Risk:** Low

**Implementation:**
```csharp
private record CacheSnapshot(
    ImmutableDictionary<string, FeatureGroupDefinition> Groups,
    ImmutableDictionary<string, FeatureDefinition> Features
);

private volatile CacheSnapshot _cache = new(
    ImmutableDictionary<string, FeatureGroupDefinition>.Empty.WithComparers(StringComparer.Ordinal),
    ImmutableDictionary<string, FeatureDefinition>.Empty.WithComparers(StringComparer.Ordinal)
);

// Atomic swap (single write)
_cache = new CacheSnapshot(newGroupCache.ToImmutable(), newFeatureCache.ToImmutable());

// Read
var snapshot = _cache;
return snapshot.Features.GetValueOrDefault(name);
```

### Solution 2: Accept Transient Inconsistency (Not Recommended)

**Pros:**
- No code changes
- Microsecond window unlikely to hit

**Cons:**
- Data integrity violation possible
- Inconsistent with consistency guarantees
- Silent corruption risk

**Effort:** None
**Risk:** Medium (data corruption under load)

### Solution 3: Use Interlocked.CompareExchange with Versioning

**Pros:**
- Lock-free atomic update
- Industry standard pattern

**Cons:**
- Complex retry logic
- Overkill for this use case

**Effort:** Medium (8-12 hours)
**Risk:** Medium (complexity)

## Recommended Action

**IMPLEMENT SOLUTION 1** - Single volatile struct wrapping both caches.

- Eliminates race condition completely
- Maintains lock-free performance
- Simple, proven pattern
- Minimal refactoring needed

## Technical Details

### Affected Files
- `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs`

### Affected Methods
- `_UpdateInMemoryStoreCacheAsync` (lines 201-241) - swap logic
- `GetOrDefaultAsync` (lines 57-79) - feature reads
- `GetFeaturesAsync` (lines 81-103) - bulk feature reads
- `GetGroupsAsync` (lines 105-129) - group reads

### Related Code
Lines 138-145: Current dual volatile field declarations

## Acceptance Criteria

- [ ] `CacheSnapshot` record created with both dictionaries
- [ ] Single volatile field `_cache` replaces `_groupMemoryCache` + `_featureMemoryCache`
- [ ] All read methods updated to access via snapshot
- [ ] Swap logic uses single assignment (atomic)
- [ ] Concurrency test added validating no mismatched reads
- [ ] Code compiles and existing tests pass

## Work Log

### 2026-01-15
- **Discovered:** Code review identified non-atomic swap in PR #146
- **Analyzed:** Confirmed race condition window between lines 239-240
- **Impact:** Medium severity (microsecond window but data integrity risk)

## Resources

- PR: #146
- File: `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs:239-240`
- Pattern reference: `DynamicSettingDefinitionStore` (uses single Dictionary, no dual-swap issue)
- .NET Memory Model: [Microsoft Docs - volatile](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/volatile)
