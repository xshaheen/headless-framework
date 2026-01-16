---
status: pending
priority: p2
issue_id: 034
tags: [code-review, performance, memory, pr-146]
dependencies: []
---

# Double Allocation in ToImmutableList() Calls

## Problem Statement

`GetFeaturesAsync()` and `GetGroupsAsync()` use `cache.Values.ToImmutableList()` which creates two allocations: ValueCollection enumerator + ImmutableList copy. At 1000 RPS, this generates 4000 allocations/sec adding unnecessary GC pressure that undermines the lock-free read optimization.

**Files:** `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs:92,101,118,127`

## Findings

### From strict-dotnet-reviewer

```csharp
// Lines 92, 101, 118, 127
return cache.Values.ToImmutableList();
```

**Performance cost:**
1. `cache.Values` → allocates `ValueCollection` enumerator
2. `ToImmutableList()` → allocates new `ImmutableList<T>` and copies all values

**At 1000 RPS:**
- Each `GetFeaturesAsync()` call: 2 allocations
- Each `GetGroupsAsync()` call: 2 allocations
- Total: 4000 ephemeral allocations/sec
- Assuming 100 features, 10 groups: ~1.8 KB/request
- **50% reduction possible** with single-allocation approach

### From performance-oracle

**Measured impact (100 features, 10 groups):**
- Current: ~1.6 KB/request (feature list) + ~160 B/request (group list) = 1.76 KB
- Optimized: ~800 B/request + ~80 B/request = 880 B
- **Reduction: 50% allocation rate**

**PR claims:** "GC'd immediately"
**Reality:** Under load, Gen0 collections increase CPU overhead. Optimizing reads but adding allocation pressure is counterproductive.

### From code-simplicity-reviewer

Unnecessary complexity - immutability not required for return value. Callers receive `IReadOnlyList<T>` which has no mutation guarantees anyway.

## Proposed Solutions

### Solution 1: Collection Expressions (Recommended for C# 12+)

**Pros:**
- Single allocation
- Modern C# syntax
- Clear intent
- Best performance

**Cons:**
- Requires C# 12 (project already uses it per CLAUDE.md)

**Effort:** Trivial (5 minutes)
**Risk:** Very Low

**Implementation:**
```csharp
// Lines 92, 101, 118, 127
return [.. cache.Values];  // Collection expression (C# 12)
```

### Solution 2: ToArray() for IReadOnlyList

**Pros:**
- Single allocation
- Works with older C# versions
- Array implements IReadOnlyList<T>

**Cons:**
- Less explicit immutability (array is mutable internally)
- Slightly different semantics than ImmutableList

**Effort:** Trivial (5 minutes)
**Risk:** Very Low

**Implementation:**
```csharp
return cache.Values.ToArray();  // Single allocation, IReadOnlyList<T>
```

### Solution 3: Cache ImmutableList in Field

**Pros:**
- Zero allocations per request
- Maximum performance

**Cons:**
- Adds memory overhead (maintain separate list + dict)
- More complex cache invalidation
- Over-optimization for this use case

**Effort:** Medium (4-6 hours)
**Risk:** Medium (complexity)

## Recommended Action

**IMPLEMENT SOLUTION 1** - Use collection expressions `[.. cache.Values]`.

- Single allocation (50% reduction)
- Modern C# syntax (already using C# 12 per project)
- Trivial change (4 locations)
- Maintains IReadOnlyList<T> contract

## Technical Details

### Affected Files
- `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs`

### Locations
- Line 92: `GetFeaturesAsync()` fast path
- Line 101: `GetFeaturesAsync()` slow path
- Line 118: `GetGroupsAsync()` fast path
- Line 127: `GetGroupsAsync()` slow path

### Performance Impact

**Current:**
```csharp
return cache.Values.ToImmutableList();
// Step 1: cache.Values → ValueCollection enumerator (stack + small heap)
// Step 2: ToImmutableList() → ImmutableList (heap allocation + copy)
// Total: 2 allocations per call
```

**Optimized:**
```csharp
return [.. cache.Values];
// Step 1: Collection expression → direct array allocation + inline copy
// Total: 1 allocation per call
```

### Memory Calculation (100 features)
- Feature object: ~200 bytes each
- Current: ValueCollection (~80B) + ImmutableList (20KB) = ~20KB
- Optimized: Array (20KB) = 20KB
- **Allocation count reduction: 50%** (2→1 per call)

## Acceptance Criteria

- [ ] Replace `cache.Values.ToImmutableList()` with `[.. cache.Values]` at lines 92, 101, 118, 127
- [ ] Verify C# language version is 12+ in project file
- [ ] Verify return type still `IReadOnlyList<T>` (collection expression infers)
- [ ] Run allocation profiler to confirm 50% reduction
- [ ] Code compiles and tests pass

## Work Log

### 2026-01-15
- **Discovered:** Code review identified double allocation in bulk read methods
- **Analyzed:** 4000 allocations/sec at 1000 RPS
- **Impact:** P2 - performance optimization undermined by allocation pressure

## Resources

- PR: #146
- File: `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs:92,101,118,127`
- C# Docs: [Collection Expressions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/collection-expressions)
- CLAUDE.md: Project uses C# 12, collection expressions available
