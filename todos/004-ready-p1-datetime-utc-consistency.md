---
status: ready
priority: p1
issue_id: "004"
tags: [code-review, data-integrity, messages, timezone, critical]
created: 2026-01-19
dependencies: []
---

# DateTime.Now vs DateTime.UtcNow Inconsistency

## Problem Statement

Mix of `DateTime.Now` (local time) and `DateTime.UtcNow` causes 5-hour timezone skew in message timestamps, breaking expiration and SLA monitoring.

**Why Critical:** Messages expire too early/late, time-based operations fail across timezones, audit logs corrupted.

## Evidence from Reviews

**Data Integrity Guardian (Agent ac80be5):**
```csharp
// IDataStorage.PostgreSql.cs:106
Added = DateTime.Now,  // ❌ LOCAL time

// ISubscribeInvoker.Default.cs:148
var timestamp = new DateTimeOffset(mediumMessage.Added, TimeSpan.Zero);  // ❌ Assumes UTC!
```

**Data Corruption Scenario:**
- Server in EST publishes message at 2026-01-19 15:00 EST
- `Added` stored as `2026-01-19 15:00` (no timezone)
- Consumer in UTC reads as `2026-01-19 15:00 UTC`
- **5-hour skew in all calculations**

## All Occurrences

**Files with DateTime.Now:**
- `IDataStorage.PostgreSql.cs`: Lines 43, 45, 106, 150, 151, 165, 259, 310, 361
- `ICapPublisher.Default.cs`: Line 147
- `IMessageSender.Default.cs`: Lines 72, 81
- `ISubscribeExector.Default.cs`: Lines 144, 159

**Total:** 14 occurrences must be changed

## Proposed Solutions

### Option 1: Use TimeProvider (Recommended)
**Effort:** Medium
**Risk:** Low - testable, .NET 8+ standard

```csharp
// Inject TimeProvider via DI
private readonly TimeProvider _timeProvider;

public DataStorage(TimeProvider timeProvider)
{
    _timeProvider = timeProvider;
}

// Use throughout
Added = _timeProvider.GetUtcNow().UtcDateTime
ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(ttl)
```

**Benefits:**
- Built-in .NET abstraction (no custom IClock needed)
- Easily mockable for tests (`new FakeTimeProvider()`)
- Standard pattern across .NET ecosystem

### Option 2: Quick Fix with DateTime.UtcNow
**Effort:** Small
**Risk:** Medium - harder to test

Global replace `DateTime.Now` → `DateTime.UtcNow` (not testable)

## Technical Details

**Affected Files (all must be fixed):**
- `src/Framework.Messages.PostgreSql/IDataStorage.PostgreSql.cs`
- `src/Framework.Messages.Core/Internal/ICapPublisher.Default.cs`
- `src/Framework.Messages.Core/Internal/IMessageSender.Default.cs`
- `src/Framework.Messages.Core/Internal/ISubscribeExector.Default.cs`

**Database Schema:**
- Ensure `Added`, `ExpiresAt` columns store UTC
- Add comment: `-- Stored in UTC`

## Acceptance Criteria

- [ ] Add `TimeProvider` to DI container (register as singleton)
- [ ] Inject `TimeProvider` into all classes using `DateTime.Now`
- [ ] Replace all 14 `DateTime.Now` with `_timeProvider.GetUtcNow().UtcDateTime`
- [ ] Verify storage layer uses UTC parameters
- [ ] Add unit test: `should_store_timestamps_in_utc` (using `FakeTimeProvider`)
- [ ] Add integration test: Deploy in EST, verify UTC storage
- [ ] Document in README: "All timestamps in UTC via TimeProvider"
- [ ] Consider migration for existing data (if applicable)

## Work Log

- **2026-01-19:** Issue identified during data integrity review
- **2026-01-19:** Counted 14 occurrences across 4 files

## Resources

- Data Integrity Review: Agent ac80be5
- .NET Docs: https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider
- Testing: `Microsoft.Extensions.TimeProvider.Testing` NuGet (FakeTimeProvider)

### 2026-01-19 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
