# Add Audit Fields to SettingValueRecord

**Date:** 2026-01-10
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, dotnet, settings, audit, agent-native

---

## Problem Statement

`SettingValueRecord.cs` (line 39) has TODO for audit fields:

```csharp
// TODO: Add DateCreated, DateUpdated, CreatedBy, UpdatedBy, ...
```

**Why it matters:**
- No visibility into who changed settings or when
- No audit trail for compliance
- Agent-native gap: agents cannot query change history

---

## Proposed Solution

Add standard audit fields:

```csharp
public sealed class SettingValueRecord
{
    // Existing fields...

    public DateTimeOffset DateCreated { get; init; }
    public DateTimeOffset DateUpdated { get; set; }
    public string? CreatedBy { get; init; }
    public string? UpdatedBy { get; set; }
}
```

Consider also:
- `GetHistoryAsync(settingName, providerName)` API
- Event sourcing for full version history

- **Effort:** Medium (includes migration)
- **Risk:** Low

---

## Technical Details

**Affected Files:**
- `src/Framework.Settings.Core/Entities/SettingValueRecord.cs`
- `src/Framework.Settings.Core/Values/SettingValueStore.cs` (SetAsync)
- EF migrations

---

## Acceptance Criteria

- [ ] Add audit fields to SettingValueRecord
- [ ] Populate on insert/update
- [ ] Add database migration
- [ ] Consider exposing via API for agent access

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-10 | Created | From code review + agent-native review |
