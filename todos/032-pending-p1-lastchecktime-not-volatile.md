---
status: pending
priority: p1
issue_id: 032
tags: [code-review, concurrency, thread-safety, pr-146]
dependencies: []
---

# _lastCheckTime Field Not Volatile - Race Condition in Fast Path

## Problem Statement

`_lastCheckTime` field (line 136) is read outside lock in fast-path (line 284) but not marked `volatile`, creating potential torn read on 32-bit platforms and stale read visibility issues. Fast-path check `_IsUpdateMemoryCacheRequired()` may return incorrect staleness state.

**File:** `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs:136`

## Findings

### From strict-dotnet-reviewer

```csharp
// Line 136 - NOT volatile
private DateTimeOffset? _lastCheckTime;

// Line 158 - Write (inside lock)
_lastCheckTime = timeProvider.GetUtcNow();

// Line 284 - Read (OUTSIDE lock, fast path)
if (_lastCheckTime is null) return true;
```

**Problem:** Fast-path readers may see:
1. Stale `_lastCheckTime` (benign - triggers slow path unnecessarily)
2. Torn read on 32-bit platforms (`DateTimeOffset` = 12 bytes, not atomic)

### From performance-oracle

**Impact on 64-bit:** Likely benign (pointer-sized reads atomic)
**Impact on 32-bit/.NET Framework:** Possible corruption → invalid cache staleness check

Race scenario:
- Thread A (fast path): Checks `_IsUpdateMemoryCacheRequired()` - reads non-volatile `_lastCheckTime`
- Thread B (inside lock): Updates `_lastCheckTime` to new value
- Thread A: May not see update due to cache coherence delay

### From architecture-strategist

Memory visibility issue violates happens-before relationship between cache refresh and staleness check. Thread executing fast path may not observe latest `_lastCheckTime` written by refresh thread.

## Proposed Solutions

### Solution 1: Use Interlocked for Ticks (Recommended)

**Pros:**
- Atomic on all platforms (long = 64-bit)
- No torn reads possible
- Well-understood pattern

**Cons:**
- Requires conversion to/from Ticks
- Slightly more verbose

**Effort:** Small (1-2 hours)
**Risk:** Very Low

**Implementation:**
```csharp
private long _lastCheckTicks = 0; // Use Ticks (atomic on all platforms)

// Write (inside lock)
Interlocked.Exchange(ref _lastCheckTicks, timeProvider.GetUtcNow().Ticks);

// Read (fast path)
private bool _IsUpdateMemoryCacheRequired()
{
    var lastCheckTicks = Interlocked.Read(ref _lastCheckTicks);
    if (lastCheckTicks == 0) return true;

    var lastCheck = new DateTimeOffset(lastCheckTicks, TimeSpan.Zero);
    var elapsed = timeProvider.GetUtcNow().Subtract(lastCheck);
    return elapsed > _options.DynamicDefinitionsMemoryCacheExpiration;
}
```

### Solution 2: Mark _lastCheckTime as Volatile

**Pros:**
- Minimal code change
- Standard pattern

**Cons:**
- `DateTimeOffset?` still 12 bytes (torn read risk on 32-bit)
- Not guaranteed atomic on all platforms

**Effort:** Trivial (5 minutes)
**Risk:** Medium (doesn't solve 32-bit torn read)

**Implementation:**
```csharp
private volatile DateTimeOffset? _lastCheckTime;
```

### Solution 3: Use Volatile.Read/Write

**Pros:**
- Explicit memory barriers
- Clear intent

**Cons:**
- More verbose
- Same torn read issue on 32-bit

**Effort:** Small (1 hour)
**Risk:** Medium

## Recommended Action

**IMPLEMENT SOLUTION 1** - Use `Interlocked` with Ticks.

- Guaranteed atomic on all platforms
- No torn reads possible
- Performance overhead negligible (fast path already doing DateTimeOffset math)
- Proven pattern in .NET concurrency code

## Technical Details

### Affected Files
- `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs`

### Affected Code
- Line 136: Field declaration
- Line 158, 165: Writes to `_lastCheckTime`
- Lines 283-290: `_IsUpdateMemoryCacheRequired()` reads

### Platform-Specific Behavior
- **64-bit .NET:** Likely works (reference-sized reads atomic)
- **32-bit .NET Framework:** Torn read possible (`DateTimeOffset` = 12 bytes)
- **ARM/mobile:** Cache coherence delays may cause stale reads

## Acceptance Criteria

- [ ] Change `_lastCheckTime` to `long _lastCheckTicks`
- [ ] Use `Interlocked.Exchange` for writes
- [ ] Use `Interlocked.Read` for reads in `_IsUpdateMemoryCacheRequired()`
- [ ] Convert Ticks to DateTimeOffset where needed
- [ ] Add unit test for concurrent staleness checks
- [ ] Code compiles and tests pass

## Work Log

### 2026-01-15
- **Discovered:** Code review identified non-volatile field read in fast path
- **Analyzed:** Confirmed torn read risk on 32-bit platforms
- **Impact:** High severity for correctness (P1 despite low probability)

## Resources

- PR: #146
- File: `src/Framework.Features.Core/Definitions/DynamicFeatureDefinitionStore.cs:136,284`
- .NET Docs: [Interlocked Class](https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked)
- Reference: `AsyncDuplicateLockTests.cs` uses similar Interlocked patterns
