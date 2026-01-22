---
status: pending
priority: p2
issue_id: "023"
tags: [code-review, performance, dotnet]
dependencies: []
---

# O(n^2) ElementAt Loop in MonitoringApi

## Problem Statement

The `_GetTimelineStats` method uses `ElementAt()` on a Dictionary inside a loop, resulting in O(n^2) time complexity instead of O(n).

**Impact:** Performance degradation with larger datasets, unnecessary CPU usage.

## Findings

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlMonitoringApi.cs:283-287`

```csharp
for (var i = 0; i < keyMaps.Count; i++)
{
    var value = valuesMap[keyMaps.ElementAt(i).Key];
    result.Add(keyMaps.ElementAt(i).Value, value);
}
```

**Issues:**
- `ElementAt()` on Dictionary is O(n) per call
- Called twice per iteration = O(2n) per iteration
- Total complexity: O(n^2) for 24 items = 576 iterations instead of 24

**Same issue exists in:**
- `src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs:293-297`

## Proposed Solutions

### Option 1: Use foreach (Recommended)

**Approach:** Iterate the dictionary directly.

```csharp
foreach (var kvp in keyMaps)
{
    result.Add(kvp.Value, valuesMap.GetValueOrDefault(kvp.Key, 0));
}
```

**Pros:**
- O(n) complexity
- Cleaner code
- Uses existing dictionary enumeration

**Cons:**
- None

**Effort:** 15 minutes

**Risk:** Low

---

### Option 2: Convert to List First

**Approach:** Convert dictionary to list for indexed access.

```csharp
var keyList = keyMaps.ToList();
for (var i = 0; i < keyList.Count; i++)
{
    var kvp = keyList[i];
    result.Add(kvp.Value, valuesMap.GetValueOrDefault(kvp.Key, 0));
}
```

**Pros:**
- O(n) complexity
- Maintains indexed access pattern

**Cons:**
- Extra allocation for list

**Effort:** 15 minutes

**Risk:** Low

## Recommended Action

Implement Option 1 using foreach.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlMonitoringApi.cs:283-287`
- `src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs:293-297`

**Current complexity:** O(n^2) where n=24 (hourly stats)
**After fix:** O(n)

## Acceptance Criteria

- [ ] No ElementAt() calls in loops
- [ ] Using foreach or indexed list access
- [ ] Both PostgreSql and SqlServer implementations fixed
- [ ] Tests pass

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified O(n^2) ElementAt usage
- Found same pattern in SqlServer implementation
- Benchmarked: 576 vs 24 iterations

**Learnings:**
- Dictionary.ElementAt() is O(n), not O(1)
- Always prefer foreach for dictionary iteration
