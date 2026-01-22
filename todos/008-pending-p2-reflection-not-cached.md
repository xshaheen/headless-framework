---
status: pending
priority: p2
issue_id: "008"
tags: [code-review, dotnet, performance]
dependencies: []
---

# Reflection Not Cached in DiagnosticObserver

## Problem Statement

`DiagnosticObserver._GetProperty` uses reflection on every diagnostic event without caching, causing performance overhead in hot path.

## Findings

**File:** `src/Headless.Messaging.SqlServer/Diagnostics/DiagnosticObserver.cs:84-87`

```csharp
private static object? _GetProperty(object? @this, string propertyName)
{
    return @this?.GetType().GetTypeInfo().GetDeclaredProperty(propertyName)?.GetValue(@this);
}
```

Called from:
- `_TryGetSqlConnection` (line 80) - on every diagnostic event
- Line 38 - getting "Operation" property

**Impact:**
- Reflection called on every transaction commit/rollback
- `GetDeclaredProperty` is relatively expensive
- Under high throughput, this adds up

## Proposed Solutions

### Option 1: Cache PropertyInfo (Recommended)

**Approach:** Use ConcurrentDictionary to cache PropertyInfo by (Type, PropertyName).

```csharp
private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> _PropertyCache = new();

private static object? _GetProperty(object? @this, string propertyName)
{
    if (@this == null) return null;

    var type = @this.GetType();
    var prop = _PropertyCache.GetOrAdd(
        (type, propertyName),
        key => key.Item1.GetTypeInfo().GetDeclaredProperty(key.Item2)
    );

    return prop?.GetValue(@this);
}
```

**Pros:**
- One-time reflection per type/property combo
- Thread-safe caching
- Simple change

**Cons:**
- Small memory overhead for cache

**Effort:** 30 minutes

**Risk:** Low

---

### Option 2: Compiled Expressions

**Approach:** Use compiled lambda expressions for property access.

**Pros:**
- Near-direct call performance
- Zero reflection after compilation

**Cons:**
- More complex code
- Higher initial compilation cost

**Effort:** 1-2 hours

**Risk:** Low

## Recommended Action

Implement Option 1 for simplicity. The diagnostic events are not extremely high-frequency (per-transaction, not per-message), so PropertyInfo caching is sufficient.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.SqlServer/Diagnostics/DiagnosticObserver.cs:75-87`

## Acceptance Criteria

- [ ] PropertyInfo is cached per (Type, PropertyName)
- [ ] Cache is thread-safe
- [ ] Performance improves under load
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Performance Oracle Agent

**Actions:**
- Identified uncached reflection
- Documented caching approaches
