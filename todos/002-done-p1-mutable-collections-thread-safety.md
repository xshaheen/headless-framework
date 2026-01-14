---
status: done
priority: p1
issue_id: "002"
tags: [code-review, thread-safety, permissions]
dependencies: []
---

# Thread Safety: Mutable Collections Exposed on PermissionDefinition

## Problem Statement

`PermissionDefinition.Providers` and `Properties` are mutable `List<string>` and `Dictionary<string, object?>` exposed as public properties. Since permission definitions are cached and shared across requests, concurrent modifications could cause race conditions or data corruption.

## Findings

**Location:** `src/Framework.Permissions.Abstractions/Models/PermissionDefinition.cs` (lines 40, 46)

```csharp
public List<string> Providers { get; }
public Dictionary<string, object?> Properties { get; }
```

Any caller can `Add()`, `Remove()`, or `Clear()` these collections. The definitions are cached in:
- `StaticPermissionDefinitionStore._lazyPermissionDefinitions`
- `DynamicPermissionDefinitionStore._permissionMemoryCache`

Both caches are singleton/long-lived, meaning all requests share the same instances.

## Proposed Solutions

### Option A: Expose as Read-Only (Recommended)
**Pros:** Simple, prevents mutations
**Cons:** Breaking change for consumers adding providers
**Effort:** Small
**Risk:** Low (may require migration for users)

```csharp
public IReadOnlyList<string> Providers => _providers;
public IReadOnlyDictionary<string, object?> Properties => _properties;
```

### Option B: Defensive Copy on Access
**Pros:** Non-breaking, safe
**Cons:** Allocation on every access
**Effort:** Small
**Risk:** Low

### Option C: Thread-Safe Collections
**Pros:** Allows concurrent modification
**Cons:** Heavier weight, may hide design issues
**Effort:** Medium
**Risk:** Medium

## Recommended Action

Use Option A: Expose as `IReadOnlyList<string>` and `IReadOnlyDictionary<string, object?>`. Add explicit mutation methods if needed for definition setup.

## Technical Details

**Affected Files:**
- `src/Framework.Permissions.Abstractions/Models/PermissionDefinition.cs`
- `src/Framework.Permissions.Abstractions/Models/PermissionGroupDefinition.cs`

## Acceptance Criteria

- [x] Collections cannot be mutated by external code
- [x] Existing code that reads collections continues to work
- [x] Add explicit methods for modification if needed

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Created from code review | Strict .NET review finding |
| 2026-01-14 | Triage approved | Status: ready |

## Resources

- Strict .NET Reviewer findings

### 2026-01-14 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
