---
status: pending
priority: p2
issue_id: "024"
tags: [code-review, performance, dotnet, reflection]
dependencies: []
---

# TypeDescriptor.GetConverter Not Cached in ExecuteScalarAsync

## Problem Statement

`TypeDescriptor.GetConverter()` is called on every `ExecuteScalarAsync` invocation without caching. This uses reflection and creates new converter instances repeatedly.

**Impact:** ~10-50μs overhead per scalar query, adds up in high-throughput monitoring scenarios.

## Findings

- **File:** `src/Headless.Messaging.PostgreSql/DbConnectionExtensions.cs:97-106`

```csharp
var returnType = typeof(T);
var converter = TypeDescriptor.GetConverter(returnType);
if (converter.CanConvertFrom(objValue.GetType()))
{
    result = (T)converter.ConvertFrom(objValue)!;
}
else
{
    result = (T)Convert.ChangeType(objValue, returnType);
}
```

**Issues:**
- `TypeDescriptor.GetConverter` performs reflection
- Called for every scalar query (COUNT, etc.)
- Monitoring dashboard makes many scalar queries

## Proposed Solutions

### Option 1: Cache Converters (Recommended)

**Approach:** Use a static ConcurrentDictionary to cache converters.

```csharp
private static readonly ConcurrentDictionary<Type, TypeConverter> _converterCache = new();

public static async Task<T> ExecuteScalarAsync<T>(...)
{
    // ...
    var returnType = typeof(T);
    var converter = _converterCache.GetOrAdd(returnType, TypeDescriptor.GetConverter);
    // ...
}
```

**Pros:**
- One-time reflection cost per type
- Thread-safe caching

**Cons:**
- Minor memory for cache (negligible)

**Effort:** 30 minutes

**Risk:** Low

---

### Option 2: Direct Casting for Known Types

**Approach:** Handle common types (int, long, string) directly without TypeDescriptor.

```csharp
if (objValue is T directCast)
    return directCast;

return returnType switch
{
    _ when returnType == typeof(int) => (T)(object)Convert.ToInt32(objValue),
    _ when returnType == typeof(long) => (T)(object)Convert.ToInt64(objValue),
    _ when returnType == typeof(string) => (T)(object)objValue.ToString()!,
    _ => (T)Convert.ChangeType(objValue, returnType)
};
```

**Pros:**
- Fastest for common types
- No reflection at all

**Cons:**
- More code
- Need to handle each type

**Effort:** 45 minutes

**Risk:** Low

## Recommended Action

Implement Option 1 for simplicity, or Option 2 if profiling shows this is a hot path.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/DbConnectionExtensions.cs:72-110`
- `src/Headless.Messaging.SqlServer/DbConnectionExtensions.cs` (same issue)

**Called from:**
- `PostgreSqlMonitoringApi.GetStatisticsAsync` (multiple COUNT queries)
- `PostgreSqlMonitoringApi._GetNumberOfMessage` (COUNT queries)

## Acceptance Criteria

- [ ] TypeDescriptor.GetConverter called once per type (cached)
- [ ] Performance improvement measurable in benchmarks
- [ ] Both PostgreSql and SqlServer fixed
- [ ] Tests pass

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified uncached reflection in ExecuteScalarAsync
- Found same pattern in SqlServer
- Estimated ~10-50μs overhead per call

**Learnings:**
- TypeDescriptor is designed for tooling, not hot paths
- ConcurrentDictionary caching is standard pattern
