---
status: pending
priority: p2
issue_id: "009"
tags: [code-review, dotnet, performance]
dependencies: []
---

# ElementAt O(n²) Loop in Monitoring API

## Problem Statement

`_GetTimelineStats` uses `Dictionary.ElementAt(i)` in a loop, causing O(n²) time complexity.

## Findings

**File:** `src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs:292-297`

```csharp
var result = new Dictionary<DateTime, int>();
for (var i = 0; i < keyMaps.Count; i++)
{
    var value = valuesMap[keyMaps.ElementAt(i).Key];    // ElementAt is O(n)
    result.Add(keyMaps.ElementAt(i).Value, value);      // Called twice!
}
```

**Impact:**
- `ElementAt()` on Dictionary is O(n) - enumerates from start each call
- With 24 hours of data, this is 24 * 24 * 2 = 1152 enumeration operations
- Dashboard queries become slow

## Proposed Solutions

### Option 1: Use foreach (Recommended)

**Approach:** Replace indexed loop with foreach enumeration.

```csharp
var result = new Dictionary<DateTime, int>(keyMaps.Count);
foreach (var (key, dateTime) in keyMaps)
{
    result[dateTime] = valuesMap.TryGetValue(key, out var count) ? count : 0;
}
```

**Pros:**
- O(n) complexity
- Cleaner code
- Pre-sized dictionary

**Cons:**
- None

**Effort:** 15 minutes

**Risk:** Low

## Recommended Action

Implement Option 1. Also fix the related inefficiency in `_GetHourlyTimelineStats` (lines 213-226) that creates an unnecessary List before converting to Dictionary.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs:292-297`
- `src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs:213-226` (related)

## Acceptance Criteria

- [ ] ElementAt replaced with foreach
- [ ] Dictionary pre-sized
- [ ] TryGetValue used for safety
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Performance Oracle Agent

**Actions:**
- Identified O(n²) pattern
- Documented O(n) fix
